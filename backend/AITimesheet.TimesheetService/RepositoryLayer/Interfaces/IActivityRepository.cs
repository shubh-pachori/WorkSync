using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.RepositoryLayer.Interfaces;

public interface IActivityRepository
{
    Task<List<Activity>> GetForUserAsync(
        Guid userId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<Activity> activities, CancellationToken ct = default);

    /// <summary>
    /// Clears a user's activities for a date range so a regenerate replaces them instead
    /// of appending a duplicate set on every run.
    /// </summary>
    Task DeleteForUserAndRangeAsync(Guid userId, DateTime from, DateTime to, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
