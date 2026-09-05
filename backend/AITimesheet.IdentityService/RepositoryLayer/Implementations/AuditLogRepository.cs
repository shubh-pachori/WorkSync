using AITimesheet.IdentityService.Data;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;

namespace AITimesheet.IdentityService.RepositoryLayer.Implementations;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IdentityDbContext _db;

    public AuditLogRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AuditLog log, CancellationToken ct = default) =>
        await _db.AuditLogs.AddAsync(log, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
