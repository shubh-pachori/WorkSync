using System.ComponentModel.DataAnnotations.Schema;

namespace AITimesheet.IdentityService.Entities;

public static class UserRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Admin = "Admin";

    public static bool IsValid(string role) => role is Employee or Manager or Admin;
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash in the encoded form produced by <see cref="Helpers.PasswordHasher"/>.
    /// Never holds a plaintext password and is never projected into a DTO.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string? AzureAdObjectId { get; set; }

    /// <summary>Employee | Manager | Admin — see <see cref="UserRoles"/>.</summary>
    public string Role { get; set; } = UserRoles.Employee;

    public Guid? ManagerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // ---- Two-factor authentication ------------------------------------------------

    /// <summary>
    /// Base32 TOTP shared secret, encrypted at rest. Present but with
    /// <see cref="TotpEnabledAt"/> null means enrolment was started and never confirmed.
    /// </summary>
    public string? TotpSecret { get; set; }

    /// <summary>When the user confirmed enrolment. Null means 2FA is off.</summary>
    public DateTime? TotpEnabledAt { get; set; }

    /// <summary>
    /// The most recent TOTP time step accepted for this user. A code stays valid for its
    /// whole window, so without this the same code could be replayed within 30 seconds.
    /// </summary>
    public long? TotpLastUsedStep { get; set; }

    [NotMapped]
    public bool IsTotpEnabled => TotpEnabledAt is not null && !string.IsNullOrEmpty(TotpSecret);
}
