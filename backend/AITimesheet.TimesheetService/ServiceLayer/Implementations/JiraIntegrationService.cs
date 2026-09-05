using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

public class JiraIntegrationService : IIntegrationService
{
    private readonly HttpClient _http;
    private readonly ILogger<JiraIntegrationService> _logger;

    public ConnectionProvider Provider => ConnectionProvider.Jira;

    public JiraIntegrationService(HttpClient http, IConfiguration config, ILogger<JiraIntegrationService> logger)
    {
        _http = http;
        _logger = logger;

        var siteUrl = config["Jira:SiteUrl"] ?? "https://your-domain.atlassian.net";
        _http.BaseAddress ??= new Uri($"{siteUrl.TrimEnd('/')}/rest/api/3/");
    }

    public async Task<IntegrationFetchResult> FetchActivitiesAsync(
        Guid userId, string accessToken, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        try
        {
            var jql = $"assignee = currentUser() AND updated >= \"{weekStart:yyyy-MM-dd}\" " +
                      $"AND updated <= \"{weekEnd:yyyy-MM-dd}\"";

            var request = new HttpRequestMessage(
                HttpMethod.Get, $"search?jql={Uri.EscapeDataString(jql)}&maxResults=100");

            // Jira basic auth expects "email:api-token"; the stored credential is that pair.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(accessToken)));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "the stored Jira credential is invalid — reconnect Jira"
                    : $"Jira returned {(int)response.StatusCode}";

                _logger.LogWarning("Jira fetch failed for user {UserId}: {Detail}.", userId, detail);
                return IntegrationFetchResult.Failed($"Could not read Jira issues: {detail}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("issues", out var issues))
            {
                return IntegrationFetchResult.Ok(Array.Empty<Activity>());
            }

            var activities = new List<Activity>();
            foreach (var issue in issues.EnumerateArray())
            {
                var key = issue.TryGetProperty("key", out var k) ? k.GetString() : null;
                if (!issue.TryGetProperty("fields", out var fields)) continue;

                var summary = fields.TryGetProperty("summary", out var s) ? s.GetString() : null;
                var status = fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var sn)
                    ? sn.GetString()
                    : null;

                // Use the issue's own updated timestamp instead of pinning everything to Monday.
                var updatedAt = fields.TryGetProperty("updated", out var u) &&
                                DateTime.TryParse(u.GetString(), out var parsed)
                    ? parsed.ToUniversalTime()
                    : weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

                activities.Add(new Activity
                {
                    UserId = userId,
                    Source = ActivitySource.JiraTicket,
                    Title = $"{key} {summary}".Trim(),
                    Status = status,
                    ExternalReference = key,
                    ActivityDate = updatedAt,
                    EstimatedHours = 2
                });
            }

            _logger.LogInformation("Jira returned {Count} issues for user {UserId}.", activities.Count, userId);
            return IntegrationFetchResult.Ok(activities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jira fetch failed for user {UserId}.", userId);
            return IntegrationFetchResult.Failed($"Jira request failed: {ex.Message}");
        }
    }
}
