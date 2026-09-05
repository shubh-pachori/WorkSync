using AITimesheet.IdentityService.DTOs;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.Helpers;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using AITimesheet.IdentityService.ServiceLayer.Interfaces;

namespace AITimesheet.IdentityService.ServiceLayer.Implementations;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly JwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ITwoFactorService _twoFactor;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepo,
        IAuditLogRepository auditRepo,
        JwtService jwtService,
        IPasswordHasher passwordHasher,
        IRefreshTokenService refreshTokens,
        ITwoFactorService twoFactor,
        ILogger<AuthService> logger)
    {
        _userRepo = userRepo;
        _auditRepo = auditRepo;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _refreshTokens = refreshTokens;
        _twoFactor = twoFactor;
        _logger = logger;
    }

    public async Task<AuthenticationResult?> LoginAsync(
        LoginRequestDto request, ClientInfo client, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepo.GetByEmailAsync(email, ct);

        // No auto-registration. An unknown email is a failed login, not a new account,
        // and the role always comes from the stored row — never from the email string.
        if (user is null)
        {
            _logger.LogWarning("Login rejected: no account for {Email}.", email);

            // Burn comparable CPU on unknown emails so response timing does not reveal
            // whether the address exists.
            _passwordHasher.Verify(request.Password, DummyHash);
            return null;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login rejected: account {UserId} is deactivated.", user.Id);
            await AuditAsync(user.Id, "Login Rejected", "Account is deactivated.", ct);
            return null;
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login rejected: bad password for {UserId}.", user.Id);
            await AuditAsync(user.Id, "Login Failed", "Incorrect password.", ct);
            return null;
        }

        // Password is correct but the session does not exist yet: hand back a token that
        // proves only this much, and wait for the second factor.
        if (user.IsTotpEnabled)
        {
            await AuditAsync(user.Id, "Password Accepted", "Awaiting two-factor verification.", ct);
            return new AuthenticationResult(AuthResponseDto.TotpRequired(_jwtService.GenerateMfaToken(user.Id)), null);
        }

        return await StartSessionAsync(user, client, "User Logged In", ct);
    }

    public async Task<AuthenticationResult?> CompleteTotpLoginAsync(
        TotpLoginRequestDto request, ClientInfo client, CancellationToken ct = default)
    {
        if (!_jwtService.TryReadMfaToken(request.MfaToken, out var userId))
        {
            _logger.LogWarning("Two-factor step rejected: invalid or expired sign-in session token.");
            return null;
        }

        var user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive || !user.IsTotpEnabled)
        {
            _logger.LogWarning("Two-factor step rejected for {UserId}: account is not eligible.", userId);
            return null;
        }

        if (!await _twoFactor.VerifyAndConsumeAsync(user, request.Code, ct))
        {
            _logger.LogWarning("Two-factor step rejected for {UserId}: invalid code.", userId);
            await AuditAsync(userId, "Two-Factor Failed", "Invalid verification code.", ct);
            return null;
        }

        return await StartSessionAsync(user, client, "User Logged In (2FA)", ct);
    }

    public async Task<AuthenticationResult?> RefreshAsync(
        string refreshToken, ClientInfo client, CancellationToken ct = default)
    {
        var rotation = await _refreshTokens.RotateAsync(refreshToken, client, ct);
        if (rotation is null) return null;

        var user = await _userRepo.GetByIdAsync(rotation.UserId, ct);
        if (user is null || !user.IsActive)
        {
            // The account went away or was disabled mid-session; do not mint a new token.
            await _refreshTokens.RevokeAllForUserAsync(rotation.UserId, RevocationReasons.SignedOut, ct);
            return null;
        }

        var (accessToken, expiresAtUtc) = _jwtService.GenerateToken(user.Id, user.Email, user.Role);

        return new AuthenticationResult(
            AuthResponseDto.SignedIn(ToDto(user), accessToken, expiresAtUtc),
            rotation.NewToken);
    }

    public async Task SignOutAsync(string? refreshToken, Guid userId, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _refreshTokens.RevokeAsync(refreshToken, RevocationReasons.SignedOut, ct);
        }

        await AuditAsync(userId, "User Logged Out", null, ct);
    }

    public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default) =>
        _userRepo.GetByIdAsync(userId, ct);

    public Task<List<User>> GetUsersByManagerIdAsync(Guid managerId, CancellationToken ct = default) =>
        _userRepo.GetByManagerIdAsync(managerId, ct);

    private async Task<AuthenticationResult> StartSessionAsync(
        User user, ClientInfo client, string auditAction, CancellationToken ct)
    {
        await AuditAsync(user.Id, auditAction, $"Signed in as {user.FullName}.", ct);

        var (accessToken, expiresAtUtc) = _jwtService.GenerateToken(user.Id, user.Email, user.Role);
        var refreshToken = await _refreshTokens.IssueAsync(user.Id, client, ct);

        return new AuthenticationResult(
            AuthResponseDto.SignedIn(ToDto(user), accessToken, expiresAtUtc),
            refreshToken);
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.Role, u.ManagerId, u.IsTotpEnabled);

    private async Task AuditAsync(Guid userId, string action, string? details, CancellationToken ct)
    {
        await _auditRepo.AddAsync(new AuditLog { UserId = userId, Action = action, Details = details }, ct);
        await _auditRepo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A well-formed hash of a value nobody knows, used only to equalise the cost of a login
    /// attempt against a non-existent account.
    /// </summary>
    private const string DummyHash =
        "v1.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
