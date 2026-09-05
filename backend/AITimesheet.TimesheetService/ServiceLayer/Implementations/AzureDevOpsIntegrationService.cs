using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

public class AzureDevOpsIntegrationService : IIntegrationService
{
    private readonly HttpClient _http;
    private readonly ILogger<AzureDevOpsIntegrationService> _logger;

    public ConnectionProvider Provider => ConnectionProvider.AzureDevOps;

    public AzureDevOpsIntegrationService(
        HttpClient http, IConfiguration config, ILogger<AzureDevOpsIntegrationService> logger)
    {
        _http = http;
        _logger = logger;

        var org = config["AzureDevOps:Organization"] ?? "your-org";
        var project = config["AzureDevOps:Project"] ?? "your-project";
        _http.BaseAddress ??= new Uri($"https://dev.azure.com/{org}/{project}/_apis/");
    }

    public async Task<IntegrationFetchResult> FetchActivitiesAsync(
        Guid userId, string accessToken, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        try
        {
            // WIQL has no parameter binding, so dates are formatted to a fixed, non-user
            // controlled shape rather than concatenated from arbitrary input.
            var wiql = new
            {
                query =
                    "SELECT [System.Id],[System.Title],[System.State],[System.ChangedDate] " +
                    "FROM WorkItems " +
                    $"WHERE [System.ChangedDate] >= '{weekStart:yyyy-MM-dd}' " +
                    $"AND [System.ChangedDate] <= '{weekEnd:yyyy-MM-dd}' " +
                    "AND [System.AssignedTo] = @Me " +
                    "ORDER BY [System.ChangedDate] DESC"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "wit/wiql?api-version=7.1")
            {
                Content = new StringContent(JsonSerializer.Serialize(wiql), Encoding.UTF8, "application/json")
            };

            // Azure DevOps PAT auth: an empty username with the PAT as the password.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}")));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    or System.Net.HttpStatusCode.NonAuthoritativeInformation
                    ? "the stored personal access token is invalid or expired — reconnect Azure DevOps"
                    : $"Azure DevOps returned {(int)response.StatusCode}";

                _logger.LogWarning("Azure DevOps fetch failed for user {UserId}: {Detail}.", userId, detail);
                return IntegrationFetchResult.Failed($"Could not read work items: {detail}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("workItems", out var workItems))
            {
                return IntegrationFetchResult.Ok(Array.Empty<Activity>());
            }

            var activities = new List<Activity>();
            foreach (var item in workItems.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement)) continue;
                var id = idElement.GetInt32();

                activities.Add(new Activity
                {
                    UserId = userId,
                    // Previously recorded as JiraTicket, which made Azure DevOps work items
                    // indistinguishable from Jira issues everywhere downstream.
                    Source = ActivitySource.WorkItem,
                    Title = $"Work item {id}",
                    ExternalReference = id.ToString(),
                    ActivityDate = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    EstimatedHours = 1
                });
            }

            _logger.LogInformation(
                "Azure DevOps returned {Count} work items for user {UserId}.", activities.Count, userId);

            return IntegrationFetchResult.Ok(activities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure DevOps fetch failed for user {UserId}.", userId);
            return IntegrationFetchResult.Failed($"Azure DevOps request failed: {ex.Message}");
        }
    }
}
