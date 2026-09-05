using System.Security.Claims;
using AITimesheet.IdentityService.DTOs;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.Security;
using AITimesheet.IdentityService.ServiceLayer.Implementations;
using AITimesheet.IdentityService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AITimesheet.IdentityService.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize] // Secure by default; anonymous access is opted into per action.
public class AuthController : ControllerBase
{
    /// <summary>
    /// Refresh cookie name. It is httpOnly so page JavaScript cannot read it, scoped to
    /// /api/auth so it is not attached to ordinary API calls, and SameSite=Strict so it is
    /// not sent on cross-site navigations.
    /// </summary>
    private const string RefreshCookieName = "ait_rt";
    private const string RefreshCookiePath = "/api/auth";

    private readonly IAuthService _authService;
    private readonly ITwoFactorService _twoFactor;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IAuthService authService, ITwoFactorService twoFactor, IWebHostEnvironment environment)
    {
        _authService = authService;
        _twoFactor = twoFactor;
        _environment = environment;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private ClientInfo Client => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());

    // ---- Sign in ---------------------------------------------------------------------

    /// <summary>
    /// Step one. Returns a session when the account has no second factor, otherwise a
    /// short-lived token to present at /login/totp.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login(
        [FromBody] LoginRequestDto request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, Client, ct);

        // One message for every failure mode, so the response cannot be used to enumerate
        // which email addresses exist.
        if (result is null) return InvalidCredentials();

        return Session(result);
    }

    /// <summary>Step two: an authenticator code, or a recovery code.</summary>
    [HttpPost("login/totp")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AuthResponseDto>> LoginTotp(
        [FromBody] TotpLoginRequestDto request, CancellationToken ct)
    {
        var result = await _authService.CompleteTotpLoginAsync(request, Client, ct);

        if (result is null)
        {
            return Problem(
                title: "Verification failed",
                detail: "That code is not valid, or the sign-in session expired. Start again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Session(result);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token, rotating the cookie. This is
    /// also how the SPA restores a session after a page reload, since the access token is
    /// held only in memory.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Refresh(CancellationToken ct)
    {
        var cookie = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(cookie)) return NoSession();

        var result = await _authService.RefreshAsync(cookie, Client, ct);
        if (result is null)
        {
            // Rotation failed: expired, unknown, or reuse detected. Clear the dead cookie
            // so the browser stops sending it.
            ClearRefreshCookie();
            return NoSession();
        }

        return Session(result);
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken ct)
    {
        await _authService.SignOutAsync(Request.Cookies[RefreshCookieName], CurrentUserId, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>Returns the caller's own profile, resolved from the token.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await _authService.GetUserByIdAsync(CurrentUserId, ct);
        if (user is null) return NotFound();
        return Ok(ToDto(user));
    }

    // ---- Two-factor management -------------------------------------------------------

    [HttpGet("totp/status")]
    public async Task<ActionResult<TotpStatusDto>> TotpStatus(CancellationToken ct)
    {
        var status = await _twoFactor.GetStatusAsync(CurrentUserId, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Begins enrolment. 2FA is not active until /totp/enable confirms a code.</summary>
    [HttpPost("totp/setup")]
    public async Task<ActionResult<TotpSetupDto>> TotpSetup(CancellationToken ct)
    {
        var result = await _twoFactor.BeginSetupAsync(CurrentUserId, ct);
        return result.Succeeded ? Ok(result.Value) : FromOutcome(result.Outcome);
    }

    /// <summary>Confirms enrolment and returns the recovery codes — shown exactly once.</summary>
    [HttpPost("totp/enable")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<RecoveryCodesDto>> TotpEnable(
        [FromBody] TotpEnableRequestDto request, CancellationToken ct)
    {
        var result = await _twoFactor.EnableAsync(CurrentUserId, request.Code, ct);
        if (!result.Succeeded) return FromOutcome(result.Outcome);

        // Enabling 2FA ends every existing session, including this one.
        ClearRefreshCookie();
        return Ok(result.Value);
    }

    [HttpPost("totp/disable")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> TotpDisable([FromBody] TotpDisableRequestDto request, CancellationToken ct)
    {
        var outcome = await _twoFactor.DisableAsync(CurrentUserId, request.Password, request.Code, ct);
        if (outcome != TwoFactorOutcome.Success) return FromOutcome(outcome);

        ClearRefreshCookie();
        return NoContent();
    }

    [HttpPost("totp/recovery-codes")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<RecoveryCodesDto>> RegenerateRecoveryCodes(
        [FromBody] TotpEnableRequestDto request, CancellationToken ct)
    {
        var result = await _twoFactor.RegenerateRecoveryCodesAsync(CurrentUserId, request.Code, ct);
        return result.Succeeded ? Ok(result.Value) : FromOutcome(result.Outcome);
    }

    // ---- Service-to-service endpoints ------------------------------------------------
    // Blocked at the gateway AND gated by a shared secret. Never reachable from a browser.

    [HttpGet("internal/users/{userId:guid}")]
    [AllowAnonymous]
    [InternalApiKey]
    public async Task<ActionResult<UserDto>> GetUserInternal(Guid userId, CancellationToken ct)
    {
        var user = await _authService.GetUserByIdAsync(userId, ct);
        if (user is null) return NotFound();
        return Ok(ToDto(user));
    }

    [HttpGet("internal/users/manager/{managerId:guid}")]
    [AllowAnonymous]
    [InternalApiKey]
    public async Task<ActionResult<List<UserDto>>> GetUsersByManagerInternal(Guid managerId, CancellationToken ct)
    {
        var users = await _authService.GetUsersByManagerIdAsync(managerId, ct);
        return Ok(users.Select(ToDto).ToList());
    }

    // ---- Helpers ---------------------------------------------------------------------

    /// <summary>
    /// Writes the refresh token to its cookie (never the body) and returns the response.
    /// </summary>
    private ActionResult<AuthResponseDto> Session(AuthenticationResult result)
    {
        if (result.RefreshToken is not null)
        {
            SetRefreshCookie(result.RefreshToken);
        }

        return Ok(result.Response);
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            // Browsers treat http://localhost as a secure context, but a plain-HTTP
            // deployment would silently drop a Secure cookie, so it is relaxed only in
            // Development.
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = DateTimeOffset.UtcNow.Add(RefreshTokenService.Lifetime),
            IsEssential = true
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Append(RefreshCookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = DateTimeOffset.UnixEpoch,
            IsEssential = true
        });

    private ObjectResult InvalidCredentials() => Problem(
        title: "Invalid credentials",
        detail: "The email or password is incorrect.",
        statusCode: StatusCodes.Status401Unauthorized);

    private ObjectResult NoSession() => Problem(
        title: "No active session",
        detail: "Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);

    private ObjectResult FromOutcome(TwoFactorOutcome outcome) => outcome switch
    {
        TwoFactorOutcome.UserNotFound => Problem(
            title: "Account not found", statusCode: StatusCodes.Status404NotFound),

        TwoFactorOutcome.InvalidCode => Problem(
            title: "Invalid code",
            detail: "That code is not valid. Check your authenticator app and try again.",
            statusCode: StatusCodes.Status400BadRequest),

        TwoFactorOutcome.InvalidPassword => Problem(
            title: "Invalid password",
            detail: "Your current password is required to change this setting.",
            statusCode: StatusCodes.Status403Forbidden),

        TwoFactorOutcome.NotEnrolled => Problem(
            title: "Setup not started",
            detail: "Start setup again to get a fresh QR code.",
            statusCode: StatusCodes.Status409Conflict),

        TwoFactorOutcome.AlreadyEnabled => Problem(
            title: "Already enabled",
            detail: "Two-factor authentication is already on. Disable it first to re-enrol.",
            statusCode: StatusCodes.Status409Conflict),

        TwoFactorOutcome.NotEnabled => Problem(
            title: "Not enabled",
            detail: "Two-factor authentication is not enabled for this account.",
            statusCode: StatusCodes.Status409Conflict),

        _ => Problem(title: "Request failed", statusCode: StatusCodes.Status400BadRequest)
    };

    private static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.Role, u.ManagerId, u.IsTotpEnabled);
}
