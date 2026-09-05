using System.ComponentModel.DataAnnotations;

namespace AITimesheet.IdentityService.DTOs;

/// <summary>
/// Step one of login. FullName and AzureAdObjectId are deliberately absent: the display
/// name comes from the stored user record, and a login request cannot create an account.
/// </summary>
public record LoginRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid address.")]
    [StringLength(200)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Step two, for accounts with two-factor authentication enabled.</summary>
public record TotpLoginRequestDto
{
    [Required(ErrorMessage = "The sign-in session token is required.")]
    public string MfaToken { get; init; } = string.Empty;

    /// <summary>A six-digit authenticator code, or a recovery code in XXXXX-XXXXX form.</summary>
    [Required(ErrorMessage = "A verification code is required.")]
    [StringLength(20, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public record UserDto(Guid Id, string FullName, string Email, string Role, Guid? ManagerId, bool TotpEnabled);

/// <summary>
/// The result of a login attempt.
///
/// When <see cref="RequiresTotp"/> is true only <see cref="MfaToken"/> is populated: the
/// password was correct but the session does not exist yet. The refresh token is never in
/// this body — it is set as an httpOnly cookie the browser's JavaScript cannot read.
/// </summary>
public record AuthResponseDto
{
    public bool RequiresTotp { get; init; }
    public string? MfaToken { get; init; }

    public UserDto? User { get; init; }
    public string? AccessToken { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }

    public static AuthResponseDto TotpRequired(string mfaToken) =>
        new() { RequiresTotp = true, MfaToken = mfaToken };

    public static AuthResponseDto SignedIn(UserDto user, string accessToken, DateTime expiresAtUtc) =>
        new() { RequiresTotp = false, User = user, AccessToken = accessToken, ExpiresAtUtc = expiresAtUtc };
}

// ---- Two-factor management -------------------------------------------------------

public record TotpStatusDto(bool Enabled, DateTime? EnabledAt, int RecoveryCodesRemaining);

/// <summary>Returned when enrolment starts. The secret is shown once, for manual entry.</summary>
public record TotpSetupDto(string Secret, string OtpAuthUri);

public record TotpEnableRequestDto
{
    [Required(ErrorMessage = "A verification code is required.")]
    [StringLength(10, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

/// <summary>Disabling 2FA re-checks the password — a hijacked session must not be able to.</summary>
public record TotpDisableRequestDto
{
    [Required(ErrorMessage = "Your password is required.")]
    [StringLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "A verification code is required.")]
    [StringLength(20, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

/// <summary>Shown exactly once; only hashes are stored.</summary>
public record RecoveryCodesDto(IReadOnlyList<string> Codes);
