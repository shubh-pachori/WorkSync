using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.RepositoryLayer.Interfaces;

public interface IRecoveryCodeRepository
{
    Task<List<RecoveryCode>> GetUnusedForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountUnusedAsync(Guid userId, CancellationToken ct = default);
    Task ReplaceAllForUserAsync(Guid userId, IEnumerable<RecoveryCode> codes, CancellationToken ct = default);
    Task DeleteAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
