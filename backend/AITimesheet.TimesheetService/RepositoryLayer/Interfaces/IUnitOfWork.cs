namespace AITimesheet.TimesheetService.RepositoryLayer.Interfaces;

/// <summary>
/// Runs several repository operations as one atomic unit. Generating a timesheet deletes
/// the previous week's rows and inserts new ones; before this, those were separate
/// SaveChanges calls, so a failure part-way left the user with no timesheet at all.
/// </summary>
public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
