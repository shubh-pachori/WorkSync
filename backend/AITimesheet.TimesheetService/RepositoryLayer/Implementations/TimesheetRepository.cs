using AITimesheet.TimesheetService.Data;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.TimesheetService.RepositoryLayer.Implementations;

public class TimesheetRepository : ITimesheetRepository
{
    private readonly TimesheetDbContext _db;

    public TimesheetRepository(TimesheetDbContext db)
    {
        _db = db;
    }

    public async Task<Timesheet?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Timesheets
            .Include(t => t.Entries)
            .Include(t => t.Approval)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Timesheet?> GetByUserAndWeekAsync(
        Guid userId, DateOnly weekStart, CancellationToken ct = default) =>
        await _db.Timesheets
            .Include(t => t.Entries)
            .Include(t => t.Approval)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.WeekStartDate == weekStart, ct);

    public async Task<List<Timesheet>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Timesheets
            .AsNoTracking()
            .Include(t => t.Entries)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.WeekStartDate)
            .ToListAsync(ct);

    public async Task<List<Timesheet>> GetByUsersAsync(List<Guid> userIds, CancellationToken ct = default) =>
        await _db.Timesheets
            .AsNoTracking()
            .Include(t => t.Entries)
            .Where(t => userIds.Contains(t.UserId))
            .OrderByDescending(t => t.WeekStartDate)
            .ToListAsync(ct);

    public async Task<List<Timesheet>> GetPendingApprovalsAsync(
        List<Guid> employeeIds, CancellationToken ct = default) =>
        await _db.Timesheets
            .Include(t => t.Entries)
            .Include(t => t.Approval)
            .Where(t => t.Status == TimesheetStatus.Submitted && employeeIds.Contains(t.UserId))
            .OrderBy(t => t.SubmittedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Timesheet timesheet, CancellationToken ct = default) =>
        await _db.Timesheets.AddAsync(timesheet, ct);

    public Task UpdateAsync(Timesheet timesheet, CancellationToken ct = default)
    {
        _db.Timesheets.Update(timesheet);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Timesheet timesheet, CancellationToken ct = default)
    {
        // Entries and the approval row cascade (see TimesheetDbContext).
        _db.Timesheets.Remove(timesheet);
        return Task.CompletedTask;
    }

    public async Task AddApprovalAsync(Approval approval, CancellationToken ct = default) =>
        await _db.Approvals.AddAsync(approval, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
