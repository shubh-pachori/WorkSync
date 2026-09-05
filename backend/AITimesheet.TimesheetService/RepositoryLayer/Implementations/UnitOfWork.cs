using AITimesheet.TimesheetService.Data;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.TimesheetService.RepositoryLayer.Implementations;

public class UnitOfWork : IUnitOfWork
{
    private readonly TimesheetDbContext _db;

    public UnitOfWork(TimesheetDbContext db)
    {
        _db = db;
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        // The execution strategy may retry the whole block on a transient failure, so the
        // transaction has to be created inside it.
        var strategy = _db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                await operation(token);
                await transaction.CommitAsync(token);
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }, ct);
    }
}
