namespace AITimesheet.IdentityService.ServiceLayer.Interfaces;

/// <summary>Coarse client details recorded on a refresh token for the audit trail.</summary>
public record ClientInfo(string? Ip, string? UserAgent);

public record RotationResult(Guid UserId, string NewToken);

public interface IRefreshTokenService
{
    /// <summary>
    /// Issues the first token of a new family (a fresh login). Returns the raw token, which
    /// is the only time it exists outside the browser — the database keeps a hash.
    /// </summary>
    Task<string> IssueAsync(Guid userId, ClientInfo client, CancellationToken ct = default);

    /// <summary>
    /// Validates and rotates a token. Returns null when the token is unknown, expired or
    /// already used — and in that last case revokes the whole family, because a token being
    /// presented twice means a copy of it escaped.
    /// </summary>
    Task<RotationResult?> RotateAsync(string rawToken, ClientInfo client, CancellationToken ct = default);

    /// <summary>Revokes the family a token belongs to. Used on sign-out.</summary>
    Task RevokeAsync(string rawToken, string reason, CancellationToken ct = default);

    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
}
