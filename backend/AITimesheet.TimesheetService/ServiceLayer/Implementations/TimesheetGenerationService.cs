using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

/// <summary>
/// Owns the generate-a-week workflow, lifted out of the controller so the ordering
/// guarantees (fetch, then dedupe, then one atomic write) live in one testable place.
/// </summary>
public class TimesheetGenerationService : ITimesheetGenerationService
{
    private readonly ITimesheetRepository _repo;
    private readonly IActivityRepository _activityRepo;
    private readonly IConnectionRepository _connectionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiTimesheetService _ai;
    private readonly ITokenProtector _tokenProtector;
    private readonly IEnumerable<IIntegrationService> _integrations;
    private readonly ILogger<TimesheetGenerationService> _logger;

    public TimesheetGenerationService(
        ITimesheetRepository repo,
        IActivityRepository activityRepo,
        IConnectionRepository connectionRepo,
        IUnitOfWork unitOfWork,
        IAiTimesheetService ai,
        ITokenProtector tokenProtector,
        IEnumerable<IIntegrationService> integrations,
        ILogger<TimesheetGenerationService> logger)
    {
        _repo = repo;
        _activityRepo = activityRepo;
        _connectionRepo = connectionRepo;
        _unitOfWork = unitOfWork;
        _ai = ai;
        _tokenProtector = tokenProtector;
        _integrations = integrations;
        _logger = logger;
    }

    public async Task<Timesheet> GenerateForWeekAsync(Guid userId, DateOnly weekStart, CancellationToken ct = default)
    {
        weekStart = WeekCalculator.SnapToWeekStart(weekStart);
        var weekEnd = WeekCalculator.WeekEndFor(weekStart);

        // A submitted or decided sheet is a record, not a draft — never silently replace it.
        var existing = await _repo.GetByUserAndWeekAsync(userId, weekStart, ct);
        if (existing is not null && !existing.IsEditable)
        {
            _logger.LogInformation(
                "Timesheet {TimesheetId} for week {WeekStart} is '{Status}'; returning it unchanged.",
                existing.Id, weekStart, existing.Status);
            return existing;
        }

        var activities = await CollectActivitiesAsync(userId, weekStart, weekEnd, ct);

        // The AI/fallback call happens before the transaction opens: it is an outbound
        // HTTP request and must not hold a database transaction open while it runs.
        var aiResult = await _ai.GenerateTimesheetAsync(activities, weekStart, weekEnd, ct);

        var timesheet = new Timesheet
        {
            UserId = userId,
            WeekStartDate = weekStart,
            WeekEndDate = weekEnd,
            Status = TimesheetStatus.Generated,
            AiWeeklySummary = aiResult.WeeklySummary,
            MissingHourPrompts = aiResult.MissingHourPrompts,
            GeneratedAt = DateTime.UtcNow,
            Entries = aiResult.Entries.Select(e => new TimesheetEntry
            {
                EntryDate = e.Date,
                ActivityDescription = e.Description,
                Hours = e.Hours,
                DevelopmentHours = e.DevHours,
                MeetingHours = e.MeetingHours,
                ReviewHours = e.ReviewHours
            }).ToList()
        };

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Replace rather than append. Regenerating used to leave the previous week's
            // activities behind, so the same commits and meetings accumulated a fresh copy
            // on every run and polluted the chat context and activity list.
            if (existing is not null)
            {
                await _repo.DeleteAsync(existing, token);
            }

            await _activityRepo.DeleteForUserAndRangeAsync(
                userId,
                weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                weekEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                token);

            await _activityRepo.AddRangeAsync(activities, token);
            await _repo.AddAsync(timesheet, token);
            await _repo.SaveChangesAsync(token);
        }, ct);

        _logger.LogInformation(
            "Generated timesheet {TimesheetId} for user {UserId}, week {WeekStart}: {EntryCount} entries from {ActivityCount} activities.",
            timesheet.Id, userId, weekStart, timesheet.Entries.Count, activities.Count);

        return timesheet;
    }

    private async Task<List<Activity>> CollectActivitiesAsync(
        Guid userId, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct)
    {
        var connections = await _connectionRepo.GetActiveByUserAsync(userId, ct);

        if (connections.Count == 0)
        {
            _logger.LogInformation(
                "User {UserId} has no connected providers; using the demo activity set.", userId);
            return Deduplicate(DemoActivityFactory.BuildWeek(userId, weekStart));
        }

        var collected = new List<Activity>();
        var statusChanged = false;

        foreach (var connection in connections)
        {
            var service = _integrations.FirstOrDefault(i => i.Provider == connection.Provider);
            if (service is null)
            {
                _logger.LogWarning(
                    "No integration registered for provider {Provider}; skipping.", connection.Provider);
                continue;
            }

            var token = _tokenProtector.Unprotect(connection.AccessToken);
            if (string.IsNullOrEmpty(token))
            {
                connection.LastError = "Stored credential could not be read. Reconnect this provider.";
                statusChanged = true;
                continue;
            }

            var result = await service.FetchActivitiesAsync(userId, token, weekStart, weekEnd, ct);

            connection.LastSyncedAt = DateTime.UtcNow;
            connection.LastError = result.Error;
            statusChanged = true;

            if (result.Succeeded)
            {
                collected.AddRange(result.Activities);
            }
            else
            {
                // Surfaced on the Connect Accounts screen instead of being replaced with
                // plausible-looking mock data.
                _logger.LogWarning(
                    "Fetch from {Provider} for user {UserId} failed: {Error}",
                    connection.Provider, userId, result.Error);
            }
        }

        if (statusChanged)
        {
            await _connectionRepo.SaveChangesAsync(ct);
        }

        return Deduplicate(collected);
    }

    /// <summary>
    /// Collapses the same item reported twice — most often a board item returned by both
    /// the Jira and Azure DevOps connectors on the same day.
    /// </summary>
    internal static List<Activity> Deduplicate(IEnumerable<Activity> activities)
    {
        var seen = new HashSet<(ActivitySource, string, DateOnly)>();
        var result = new List<Activity>();

        foreach (var activity in activities.OrderBy(a => a.ActivityDate))
        {
            if (seen.Add(activity.DedupeKey))
            {
                result.Add(activity);
            }
        }

        return result;
    }
}
