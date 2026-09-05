using System.Net.Http.Headers;
using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

public class GraphCalendarService : IIntegrationService
{
    private readonly HttpClient _http;
    private readonly ILogger<GraphCalendarService> _logger;

    public ConnectionProvider Provider => ConnectionProvider.OutlookCalendar;

    public GraphCalendarService(HttpClient http, ILogger<GraphCalendarService> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress ??= new Uri("https://graph.microsoft.com/v1.0/");
    }

    public async Task<IntegrationFetchResult> FetchActivitiesAsync(
        Guid userId, string accessToken, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        try
        {
            var startIso = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("o");
            var endIso = weekEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).ToString("o");

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"me/calendarview?startDateTime={Uri.EscapeDataString(startIso)}" +
                $"&endDateTime={Uri.EscapeDataString(endIso)}&$orderby=start/dateTime&$top=100");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "the stored Microsoft Graph token is invalid or expired — reconnect Outlook"
                    : $"Microsoft Graph returned {(int)response.StatusCode}";

                _logger.LogWarning("Calendar fetch failed for user {UserId}: {Detail}.", userId, detail);
                return IntegrationFetchResult.Failed($"Could not read calendar events: {detail}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("value", out var events))
            {
                return IntegrationFetchResult.Ok(Array.Empty<Activity>());
            }

            var activities = new List<Activity>();
            foreach (var meeting in events.EnumerateArray())
            {
                var subject = meeting.TryGetProperty("subject", out var s) ? s.GetString() ?? "Meeting" : "Meeting";

                if (!meeting.TryGetProperty("start", out var start) ||
                    !start.TryGetProperty("dateTime", out var startValue) ||
                    !DateTime.TryParse(startValue.GetString(), out var startsAt))
                {
                    continue;
                }

                var durationHours = 0.5;
                if (meeting.TryGetProperty("end", out var end) &&
                    end.TryGetProperty("dateTime", out var endValue) &&
                    DateTime.TryParse(endValue.GetString(), out var endsAt) &&
                    endsAt > startsAt)
                {
                    durationHours = (endsAt - startsAt).TotalHours;
                }

                activities.Add(new Activity
                {
                    UserId = userId,
                    Source = ActivitySource.Meeting,
                    Title = subject,
                    ActivityDate = DateTime.SpecifyKind(startsAt, DateTimeKind.Utc),
                    EstimatedHours = Math.Round(durationHours, 2)
                });
            }

            _logger.LogInformation(
                "Microsoft Graph returned {Count} meetings for user {UserId}.", activities.Count, userId);

            return IntegrationFetchResult.Ok(activities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Calendar fetch failed for user {UserId}.", userId);
            return IntegrationFetchResult.Failed($"Microsoft Graph request failed: {ex.Message}");
        }
    }
}
