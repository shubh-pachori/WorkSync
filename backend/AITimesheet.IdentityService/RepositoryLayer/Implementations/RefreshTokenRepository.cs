using AITimesheet.IdentityService.Data;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.IdentityService.RepositoryLayer.Implementations;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _db;

    public RefreshTokenRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct = default) =>
        await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow)
                    .SetProperty(t => t.RevokedReason, reason),
                ct);

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default) =>
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow)
                    .SetProperty(t => t.RevokedReason, reason),
                ct);

    // Revoked tokens are kept for a grace period so reuse detection still has something to
    // match against; only long-dead rows are removed.
    public async Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
        await _db.RefreshTokens
            .Where(t => t.ExpiresAtUtc < olderThanUtc)
            .ExecuteDeleteAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
