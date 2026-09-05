namespace AITimesheet.IdentityService.Entities;

/// <summary>
/// A single-use backup code for a user who has lost their authenticator device.
///
/// Only a SHA-256 hash is stored. These are 50-bit random values rather than
/// user-chosen passwords, so a fast hash is appropriate — there is nothing to guess.
/// </summary>
public class RecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
}
