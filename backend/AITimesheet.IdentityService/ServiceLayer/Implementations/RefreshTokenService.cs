using System.Security.Cryptography;
using System.Text;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using AITimesheet.IdentityService.ServiceLayer.Interfaces;

namespace AITimesheet.IdentityService.ServiceLayer.Implementations;

public class RefreshTokenService : IRefreshTokenService
{
    /// <summary>How long a refresh token remains usable. Rotation restarts the clock.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// Expired rows are kept this long before being purged, so a replayed token still finds
    /// its record and triggers reuse detection rather than looking merely unknown.
    /// </summary>
    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(30);

    private const int TokenBytes = 32; // 256 bits of entropy

    private readonly IRefreshTokenRepository _repo;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(IRefreshTokenRepository repo, ILogger<RefreshTokenService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<string> IssueAsync(Guid userId, ClientInfo client, CancellationToken ct = default)
    {
        // Opportunistic cleanup; a single indexed delete on an infrequent path.
        await _repo.DeleteExpiredAsync(DateTime.UtcNow - RetentionAfterExpiry, ct);

        var raw = CreateRawToken();
        await StoreAsync(userId, raw, Guid.NewGuid(), client, ct);
        await _repo.SaveChangesAsync(ct);

        return raw;
    }

    public async Task<RotationResult?> RotateAsync(
        string rawToken, ClientInfo client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var existing = await _repo.GetByHashAsync(Hash(rawToken), ct);
        if (existing is null)
        {
            _logger.LogWarning("Refresh rejected: token not recognised.");
            return null;
        }

        // Reuse detection. A rotated token should never be seen again, so a second
        // presentation means it was captured. Kill every session in the chain rather than
        // letting the legitimate user and the attacker both hold live credentials.
        if (existing.RevokedAtUtc is not null)
        {
            _logger.LogError(
                "Refresh token reuse detected for user {UserId}; revoking family {FamilyId}.",
                existing.UserId, existing.FamilyId);

            await _repo.RevokeFamilyAsync(existing.FamilyId, RevocationReasons.ReuseDetected, ct);
            await _repo.SaveChangesAsync(ct);
            return null;
        }

        if (DateTime.UtcNow >= existing.ExpiresAtUtc)
        {
            _logger.LogInformation("Refresh rejected: token expired for user {UserId}.", existing.UserId);
            return null;
        }

        var raw = CreateRawToken();
        var newHash = Hash(raw);

        existing.RevokedAtUtc = DateTime.UtcNow;
        existing.RevokedReason = RevocationReasons.Rotated;
        existing.ReplacedByTokenHash = newHash;

        await StoreAsync(existing.UserId, raw, existing.FamilyId, client, ct);
        await _repo.SaveChangesAsync(ct);

        return new RotationResult(existing.UserId, raw);
    }

    public async Task RevokeAsync(string rawToken, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;

        var existing = await _repo.GetByHashAsync(Hash(rawToken), ct);
        if (existing is null) return;

        await _repo.RevokeFamilyAsync(existing.FamilyId, reason, ct);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        await _repo.RevokeAllForUserAsync(userId, reason, ct);
        await _repo.SaveChangesAsync(ct);
    }

    private async Task StoreAsync(Guid userId, string raw, Guid familyId, ClientInfo client, CancellationToken ct) =>
        await _repo.AddAsync(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(Lifetime),
            CreatedByIp = Truncate(client.Ip, 45),
            UserAgent = Truncate(client.UserAgent, 256)
        }, ct);

    private static string CreateRawToken() =>
        // URL-safe: this value travels in a Set-Cookie header.
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// SHA-256 is right here, unlike for passwords: the token is 256 random bits, so there
    /// is no guessing attack for a slow hash to defend against.
    /// </summary>
    internal static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
