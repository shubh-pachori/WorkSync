using System.Security.Cryptography;
using System.Text;
using AITimesheet.IdentityService.DTOs;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.Helpers;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using AITimesheet.IdentityService.ServiceLayer.Interfaces;

namespace AITimesheet.IdentityService.ServiceLayer.Implementations;

public class TwoFactorService : ITwoFactorService
{
    private const int RecoveryCodeCount = 10;

    /// <summary>Excludes I, L, O, U and 0/1 so a printed code cannot be misread.</summary>
    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    private const string Issuer = "AI Timesheet";

    private readonly IUserRepository _userRepo;
    private readonly IRecoveryCodeRepository _recoveryRepo;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ITotpService _totp;
    private readonly ISecretProtector _protector;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<TwoFactorService> _logger;

    public TwoFactorService(
        IUserRepository userRepo,
        IRecoveryCodeRepository recoveryRepo,
        IRefreshTokenService refreshTokens,
        ITotpService totp,
        ISecretProtector protector,
        IPasswordHasher passwordHasher,
        ILogger<TwoFactorService> logger)
    {
        _userRepo = userRepo;
        _recoveryRepo = recoveryRepo;
        _refreshTokens = refreshTokens;
        _totp = totp;
        _protector = protector;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<TotpStatusDto?> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return null;

        var remaining = user.IsTotpEnabled ? await _recoveryRepo.CountUnusedAsync(userId, ct) : 0;
        return new TotpStatusDto(user.IsTotpEnabled, user.TotpEnabledAt, remaining);
    }

    public async Task<TwoFactorResult<TotpSetupDto>> BeginSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return TwoFactorResult<TotpSetupDto>.Fail(TwoFactorOutcome.UserNotFound);

        if (user.IsTotpEnabled)
        {
            // Re-enrolling would silently invalidate the existing authenticator.
            return TwoFactorResult<TotpSetupDto>.Fail(TwoFactorOutcome.AlreadyEnabled);
        }

        var secret = _totp.GenerateSecret();

        // Stored immediately but inert: IsTotpEnabled stays false until a code confirms the
        // user actually captured it, so a half-finished setup cannot lock them out.
        user.TotpSecret = _protector.Protect(secret);
        user.TotpEnabledAt = null;
        user.TotpLastUsedStep = null;

        await _userRepo.UpdateAsync(user, ct);
        await _userRepo.SaveChangesAsync(ct);

        return TwoFactorResult<TotpSetupDto>.Ok(
            new TotpSetupDto(secret, _totp.BuildOtpAuthUri(secret, user.Email, Issuer)));
    }

    public async Task<TwoFactorResult<RecoveryCodesDto>> EnableAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.UserNotFound);
        if (user.IsTotpEnabled) return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.AlreadyEnabled);

        var secret = _protector.Unprotect(user.TotpSecret);
        if (string.IsNullOrEmpty(secret))
        {
            return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.NotEnrolled);
        }

        if (!_totp.TryValidate(secret, code, user.TotpLastUsedStep, out var step))
        {
            return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.InvalidCode);
        }

        user.TotpEnabledAt = DateTime.UtcNow;
        user.TotpLastUsedStep = step;
        await _userRepo.UpdateAsync(user, ct);
        await _userRepo.SaveChangesAsync(ct);

        var codes = await IssueRecoveryCodesAsync(userId, ct);

        // Turning 2FA on is a credential change: existing sessions predate it, so they end.
        await _refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.TotpEnabled, ct);

        _logger.LogInformation("User {UserId} enabled two-factor authentication.", userId);
        return TwoFactorResult<RecoveryCodesDto>.Ok(new RecoveryCodesDto(codes));
    }

    public async Task<TwoFactorOutcome> DisableAsync(
        Guid userId, string password, string code, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return TwoFactorOutcome.UserNotFound;
        if (!user.IsTotpEnabled) return TwoFactorOutcome.NotEnabled;

        // Password first: a stolen access token alone must not be able to strip 2FA.
        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Rejected 2FA disable for {UserId}: wrong password.", userId);
            return TwoFactorOutcome.InvalidPassword;
        }

        if (!await VerifyAndConsumeAsync(user, code, ct))
        {
            return TwoFactorOutcome.InvalidCode;
        }

        user.TotpSecret = null;
        user.TotpEnabledAt = null;
        user.TotpLastUsedStep = null;

        await _userRepo.UpdateAsync(user, ct);
        await _userRepo.SaveChangesAsync(ct);
        await _recoveryRepo.DeleteAllForUserAsync(userId, ct);
        await _recoveryRepo.SaveChangesAsync(ct);

        await _refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.TotpDisabled, ct);

        _logger.LogWarning("User {UserId} disabled two-factor authentication.", userId);
        return TwoFactorOutcome.Success;
    }

    public async Task<TwoFactorResult<RecoveryCodesDto>> RegenerateRecoveryCodesAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null) return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.UserNotFound);
        if (!user.IsTotpEnabled) return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.NotEnabled);

        if (!await VerifyAndConsumeAsync(user, code, ct))
        {
            return TwoFactorResult<RecoveryCodesDto>.Fail(TwoFactorOutcome.InvalidCode);
        }

        return TwoFactorResult<RecoveryCodesDto>.Ok(new RecoveryCodesDto(await IssueRecoveryCodesAsync(userId, ct)));
    }

    public async Task<bool> VerifyAndConsumeAsync(User user, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var secret = _protector.Unprotect(user.TotpSecret);

        if (!string.IsNullOrEmpty(secret) &&
            _totp.TryValidate(secret, code, user.TotpLastUsedStep, out var step))
        {
            // Record the step so this code cannot be replayed inside its own window.
            user.TotpLastUsedStep = step;
            await _userRepo.UpdateAsync(user, ct);
            await _userRepo.SaveChangesAsync(ct);
            return true;
        }

        return await TryConsumeRecoveryCodeAsync(user.Id, code, ct);
    }

    private async Task<bool> TryConsumeRecoveryCodeAsync(Guid userId, string code, CancellationToken ct)
    {
        var normalised = NormaliseRecoveryCode(code);
        if (normalised.Length == 0) return false;

        var hash = HashRecoveryCode(normalised);
        var candidates = await _recoveryRepo.GetUnusedForUserAsync(userId, ct);

        var match = candidates.FirstOrDefault(c =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(c.CodeHash), Encoding.UTF8.GetBytes(hash)));

        if (match is null) return false;

        match.UsedAtUtc = DateTime.UtcNow;
        await _recoveryRepo.SaveChangesAsync(ct);

        var remaining = await _recoveryRepo.CountUnusedAsync(userId, ct);
        _logger.LogWarning(
            "User {UserId} signed in with a recovery code; {Remaining} remain.", userId, remaining);

        return true;
    }

    private async Task<List<string>> IssueRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        var plaintext = new List<string>(RecoveryCodeCount);
        var entities = new List<RecoveryCode>(RecoveryCodeCount);

        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var code = GenerateRecoveryCode();
            plaintext.Add(code);
            entities.Add(new RecoveryCode
            {
                UserId = userId,
                CodeHash = HashRecoveryCode(NormaliseRecoveryCode(code))
            });
        }

        await _recoveryRepo.ReplaceAllForUserAsync(userId, entities, ct);
        await _recoveryRepo.SaveChangesAsync(ct);

        return plaintext;
    }

    /// <summary>Ten characters from a 30-symbol alphabet — roughly 49 bits.</summary>
    private static string GenerateRecoveryCode()
    {
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        }

        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    /// <summary>Strips the separator and case so a code works however the user types it.</summary>
    private static string NormaliseRecoveryCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string HashRecoveryCode(string normalised) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
}
