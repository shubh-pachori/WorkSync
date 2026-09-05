using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.ServiceLayer.Interfaces;

public interface ITimesheetGenerationService
{
    /// <summary>
    /// Generates or regenerates one week for one user. Idempotent: calling it repeatedly
    /// leaves exactly one timesheet and one set of activities for that week. A sheet that
    /// has already been submitted or decided is returned untouched.
    /// </summary>
    Task<Timesheet> GenerateForWeekAsync(Guid userId, DateOnly weekStart, CancellationToken ct = default);
}
