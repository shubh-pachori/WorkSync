using AITimesheet.IdentityService.DTOs;
using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.ServiceLayer.Interfaces;

public enum TwoFactorOutcome
{
    Success,
    UserNotFound,
    InvalidCode,
    InvalidPassword,
    NotEnrolled,
    AlreadyEnabled,
    NotEnabled
}

public record TwoFactorResult<T>(TwoFactorOutcome Outcome, T? Value)
{
    public bool Succeeded => Outcome == TwoFactorOutcome.Success;

    public static TwoFactorResult<T> Ok(T value) => new(TwoFactorOutcome.Success, value);
    public static TwoFactorResult<T> Fail(TwoFactorOutcome outcome) => new(outcome, default);
}

public interface ITwoFactorService
{
    Task<TotpStatusDto?> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Starts enrolment: generates a secret and the QR payload. Not yet active.</summary>
    Task<TwoFactorResult<TotpSetupDto>> BeginSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Confirms enrolment with a live code and returns single-use recovery codes.</summary>
    Task<TwoFactorResult<RecoveryCodesDto>> EnableAsync(Guid userId, string code, CancellationToken ct = default);

    Task<TwoFactorOutcome> DisableAsync(Guid userId, string password, string code, CancellationToken ct = default);

    Task<TwoFactorResult<RecoveryCodesDto>> RegenerateRecoveryCodesAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Verifies a login-time code against the user's authenticator, falling back to their
    /// recovery codes. Consumes whatever it matches so neither can be replayed.
    /// </summary>
    Task<bool> VerifyAndConsumeAsync(User user, string code, CancellationToken ct = default);
}
