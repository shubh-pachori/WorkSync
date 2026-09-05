using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.RepositoryLayer.Interfaces;

public interface IConnectionRepository
{
    Task<Connection?> GetByUserAndProviderAsync(
        Guid userId, ConnectionProvider provider, CancellationToken ct = default);

    Task<List<Connection>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Connection connection, CancellationToken ct = default);
    Task UpdateAsync(Connection connection, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
