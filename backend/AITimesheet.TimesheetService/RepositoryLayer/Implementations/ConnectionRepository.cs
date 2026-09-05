using AITimesheet.TimesheetService.Data;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.TimesheetService.RepositoryLayer.Implementations;

public class ConnectionRepository : IConnectionRepository
{
    private readonly TimesheetDbContext _db;

    public ConnectionRepository(TimesheetDbContext db)
    {
        _db = db;
    }

    public async Task<Connection?> GetByUserAndProviderAsync(
        Guid userId, ConnectionProvider provider, CancellationToken ct = default) =>
        await _db.Connections.FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct);

    // Tracked on purpose: the generation service writes LastError / LastSyncedAt back.
    public async Task<List<Connection>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Connections
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderBy(c => c.Provider)
            .ToListAsync(ct);

    public async Task AddAsync(Connection connection, CancellationToken ct = default) =>
        await _db.Connections.AddAsync(connection, ct);

    public Task UpdateAsync(Connection connection, CancellationToken ct = default)
    {
        _db.Connections.Update(connection);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
