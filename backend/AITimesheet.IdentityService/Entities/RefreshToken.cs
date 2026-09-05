namespace AITimesheet.IdentityService.Entities;

/// <summary>
/// A rotating refresh token. Only a SHA-256 hash of the value is stored, so a database
/// leak does not hand an attacker usable sessions.
///
/// Tokens are grouped into a <see cref="FamilyId"/> chain: one login starts a family, and
/// each rotation adds a link. Presenting a token that has already been rotated is the
/// signature of a stolen token being replayed, and revokes the whole family.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 of the opaque token. The token itself is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens descended from a single login share this id.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Hash of the token that superseded this one; set on rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Why it was revoked — rotation, logout, or reuse detection.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>Coarse client fingerprint, for the audit trail only.</summary>
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}

public static class RevocationReasons
{
    public const string Rotated = "Rotated";
    public const string SignedOut = "SignedOut";
    public const string ReuseDetected = "ReuseDetected";
    public const string TotpDisabled = "TotpDisabled";
    public const string TotpEnabled = "TotpEnabled";
}
