using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.RepositoryLayer.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
