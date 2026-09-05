using AITimesheet.TimesheetService.Data;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.TimesheetService.RepositoryLayer.Implementations;

public class ActivityRepository : IActivityRepository
{
    private readonly TimesheetDbContext _db;

    public ActivityRepository(TimesheetDbContext db)
    {
        _db = db;
    }

    public async Task<List<Activity>> GetForUserAsync(
        Guid userId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _db.Activities.AsNoTracking().Where(a => a.UserId == userId);

        if (from.HasValue) query = query.Where(a => a.ActivityDate >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ActivityDate <= to.Value);

        return await query.OrderByDescending(a => a.ActivityDate).ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<Activity> activities, CancellationToken ct = default) =>
        await _db.Activities.AddRangeAsync(activities, ct);

    public async Task DeleteForUserAndRangeAsync(
        Guid userId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.Activities
            .Where(a => a.UserId == userId && a.ActivityDate >= from && a.ActivityDate <= to)
            .ExecuteDeleteAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
