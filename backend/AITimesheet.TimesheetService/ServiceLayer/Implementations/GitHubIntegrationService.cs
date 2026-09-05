using System.Net.Http.Headers;
using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

public class GitHubIntegrationService : IIntegrationService
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubIntegrationService> _logger;

    public ConnectionProvider Provider => ConnectionProvider.GitHub;

    public GitHubIntegrationService(HttpClient http, ILogger<GitHubIntegrationService> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress ??= new Uri("https://api.github.com/");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AITimesheetGenerator", "1.0"));
    }

    public async Task<IntegrationFetchResult> FetchActivitiesAsync(
        Guid userId, string accessToken, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        try
        {
            // Auth goes on the request, not on HttpClient.DefaultRequestHeaders — a shared
            // client must never carry one user's credential into another user's call.
            var userRequest = new HttpRequestMessage(HttpMethod.Get, "user");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var userResponse = await _http.SendAsync(userRequest, ct);
            if (!userResponse.IsSuccessStatusCode)
            {
                return Fail(userResponse.StatusCode, "resolve the GitHub account");
            }

            using var userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(ct));
            if (!userDoc.RootElement.TryGetProperty("login", out var loginElement))
            {
                return IntegrationFetchResult.Failed("GitHub did not return an account login.");
            }

            var login = loginElement.GetString();
            var query = Uri.EscapeDataString(
                $"author:{login} author-date:{weekStart:yyyy-MM-dd}..{weekEnd:yyyy-MM-dd}");

            var request = new HttpRequestMessage(HttpMethod.Get, $"search/commits?q={query}&per_page=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Fail(response.StatusCode, "search commits");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("items", out var items))
            {
                return IntegrationFetchResult.Ok(Array.Empty<Activity>());
            }

            var activities = new List<Activity>();
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("commit", out var commit)) continue;

                var message = commit.TryGetProperty("message", out var m) ? m.GetString() ?? "Commit" : "Commit";
                var sha = item.TryGetProperty("sha", out var s) ? s.GetString() : null;

                if (!commit.TryGetProperty("author", out var author) ||
                    !author.TryGetProperty("date", out var dateElement) ||
                    !DateTime.TryParse(dateElement.GetString(), out var authoredAt))
                {
                    continue;
                }

                activities.Add(new Activity
                {
                    UserId = userId,
                    Source = ActivitySource.GitCommit,
                    Title = message.Split('\n')[0],
                    Description = message,
                    // Guard the substring: a short or missing sha used to throw here.
                    ExternalReference = sha is { Length: >= 7 } ? sha[..7] : sha,
                    ActivityDate = authoredAt.ToUniversalTime(),
                    EstimatedHours = 0.5
                });
            }

            _logger.LogInformation(
                "GitHub returned {Count} commits for user {UserId}.", activities.Count, userId);

            return IntegrationFetchResult.Ok(activities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub fetch failed for user {UserId}.", userId);
            return IntegrationFetchResult.Failed($"GitHub request failed: {ex.Message}");
        }
    }

    private IntegrationFetchResult Fail(System.Net.HttpStatusCode status, string operation)
    {
        var detail = status switch
        {
            System.Net.HttpStatusCode.Unauthorized => "the stored token is invalid or expired — reconnect GitHub",
            System.Net.HttpStatusCode.Forbidden => "the token lacks the required scope, or the rate limit was hit",
            _ => $"GitHub returned {(int)status}"
        };

        _logger.LogWarning("GitHub could not {Operation}: {Detail}.", operation, detail);
        return IntegrationFetchResult.Failed($"Could not {operation}: {detail}.");
    }
}
