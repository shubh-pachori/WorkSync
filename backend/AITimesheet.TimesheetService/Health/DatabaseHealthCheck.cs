using AITimesheet.TimesheetService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AITimesheet.TimesheetService.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly TimesheetDbContext _db;

    public DatabaseHealthCheck(TimesheetDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Timesheet database reachable.")
                : HealthCheckResult.Unhealthy("Timesheet database unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Timesheet database check threw.", ex);
        }
    }
}
