using AITimesheet.IdentityService.Data;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.IdentityService.RepositoryLayer.Implementations;

public class RecoveryCodeRepository : IRecoveryCodeRepository
{
    private readonly IdentityDbContext _db;

    public RecoveryCodeRepository(IdentityDbContext db)
    {
        _db = db;
    }

    // Tracked: the caller marks the matched code as used.
    public async Task<List<RecoveryCode>> GetUnusedForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RecoveryCodes
            .Where(c => c.UserId == userId && c.UsedAtUtc == null)
            .ToListAsync(ct);

    public async Task<int> CountUnusedAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAtUtc == null, ct);

    public async Task ReplaceAllForUserAsync(
        Guid userId, IEnumerable<RecoveryCode> codes, CancellationToken ct = default)
    {
        await DeleteAllForUserAsync(userId, ct);
        await _db.RecoveryCodes.AddRangeAsync(codes, ct);
    }

    public async Task DeleteAllForUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
