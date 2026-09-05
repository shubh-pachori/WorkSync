using AITimesheet.IdentityService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AITimesheet.IdentityService.Health;

/// <summary>
/// Liveness probe for the identity database. Implemented directly rather than pulling in
/// Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore for one call.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IdentityDbContext _db;

    public DatabaseHealthCheck(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Identity database reachable.")
                : HealthCheckResult.Unhealthy("Identity database unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Identity database check threw.", ex);
        }
    }
}
