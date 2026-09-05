using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.RepositoryLayer.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Revokes every token descended from one login. Used on sign-out and, critically, when
    /// an already-rotated token is presented again — the signature of a stolen token.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct = default);

    /// <summary>Revokes every active token for a user, across all their sessions.</summary>
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);

    Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
