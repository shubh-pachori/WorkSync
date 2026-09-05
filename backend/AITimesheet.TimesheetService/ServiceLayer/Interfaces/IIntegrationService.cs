using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.ServiceLayer.Interfaces;

/// <summary>
/// Outcome of one provider fetch. Replaces the previous contract, which returned a bare
/// list — every failure was caught and swapped for mock data, so an expired token, a 500
/// from Jira and a DNS failure all looked exactly like a healthy connection.
/// </summary>
public record IntegrationFetchResult(IReadOnlyList<Activity> Activities, string? Error)
{
    public bool Succeeded => Error is null;

    public static IntegrationFetchResult Ok(IReadOnlyList<Activity> activities) => new(activities, null);

    public static IntegrationFetchResult Failed(string error) => new(Array.Empty<Activity>(), error);
}

public interface IIntegrationService
{
    ConnectionProvider Provider { get; }

    Task<IntegrationFetchResult> FetchActivitiesAsync(
        Guid userId,
        string accessToken,
        DateOnly weekStart,
        DateOnly weekEnd,
        CancellationToken ct = default);
}
