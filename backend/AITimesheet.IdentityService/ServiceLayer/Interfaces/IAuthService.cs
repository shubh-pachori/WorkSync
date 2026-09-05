using AITimesheet.IdentityService.DTOs;
using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.ServiceLayer.Interfaces;

/// <summary>
/// A completed authentication step. Named to avoid colliding with
/// Microsoft.AspNetCore.Mvc.SignInResult. <see cref="RefreshToken"/> is populated only when a
/// session actually started; the controller turns it into an httpOnly cookie and it never
/// reaches the response body.
/// </summary>
public record AuthenticationResult(AuthResponseDto Response, string? RefreshToken);

public interface IAuthService
{
    /// <summary>
    /// Verifies the password. Returns null when the email is unknown, the password is wrong,
    /// or the account is deactivated — the caller must not distinguish between those cases.
    /// For an account with 2FA on, the result carries only a short-lived MFA token.
    /// </summary>
    Task<AuthenticationResult?> LoginAsync(LoginRequestDto request, ClientInfo client, CancellationToken ct = default);

    /// <summary>Completes a two-step login with an authenticator or recovery code.</summary>
    Task<AuthenticationResult?> CompleteTotpLoginAsync(
        TotpLoginRequestDto request, ClientInfo client, CancellationToken ct = default);

    /// <summary>Rotates a refresh token and mints a fresh access token.</summary>
    Task<AuthenticationResult?> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct = default);

    Task SignOutAsync(string? refreshToken, Guid userId, CancellationToken ct = default);

    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<User>> GetUsersByManagerIdAsync(Guid managerId, CancellationToken ct = default);
}
