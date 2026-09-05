using System.Text;
using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

/// <summary>
/// Turns raw activity into timesheet entries. Calls Azure OpenAI when it is configured and
/// falls back to a deterministic rule-based generator otherwise, so the app is fully
/// usable with no API key.
/// </summary>
public class OpenAiTimesheetService : IAiTimesheetService
{
    /// <summary>Activity kinds that count as development rather than meetings or review.</summary>
    private static readonly ActivitySource[] DevelopmentSources =
    {
        ActivitySource.GitCommit,
        ActivitySource.JiraTicket,
        ActivitySource.PullRequest,
        ActivitySource.WorkItem
    };

    private const int MaxActivitiesInPrompt = 150;
    private const double ExpectedHoursPerDay = 6;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiTimesheetService> _logger;

    public OpenAiTimesheetService(HttpClient http, IConfiguration config, ILogger<OpenAiTimesheetService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<AiTimesheetResult> GenerateTimesheetAsync(
        List<Activity> weekActivities, DateOnly weekStart, DateOnly weekEnd, CancellationToken ct = default)
    {
        var aiJson = await CallModelAsync(BuildPrompt(weekActivities, weekStart, weekEnd), ct);

        if (aiJson is null)
        {
            return BuildFallbackResult(weekActivities, weekStart, weekEnd);
        }

        try
        {
            using var doc = JsonDocument.Parse(aiJson);

            if (!doc.RootElement.TryGetProperty("entries", out var entriesElement))
            {
                _logger.LogWarning("AI response had no 'entries' array; using the fallback generator.");
                return BuildFallbackResult(weekActivities, weekStart, weekEnd);
            }

            var entries = new List<AiGeneratedEntry>();
            foreach (var e in entriesElement.EnumerateArray())
            {
                if (!e.TryGetProperty("date", out var dateElement) ||
                    !DateOnly.TryParse(dateElement.GetString(), out var date))
                {
                    continue;
                }

                // Never trust the model's arithmetic blindly: clamp to a sane day.
                var hours = Math.Clamp(ReadDouble(e, "hours"), 0, 24);

                entries.Add(new AiGeneratedEntry(
                    date,
                    e.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    hours,
                    Math.Clamp(ReadDouble(e, "devHours"), 0, 24),
                    Math.Clamp(ReadDouble(e, "meetingHours"), 0, 24),
                    Math.Clamp(ReadDouble(e, "reviewHours"), 0, 24)));
            }

            if (entries.Count == 0)
            {
                _logger.LogWarning("AI returned no usable entries; using the fallback generator.");
                return BuildFallbackResult(weekActivities, weekStart, weekEnd);
            }

            var summary = doc.RootElement.TryGetProperty("weeklySummary", out var s)
                ? s.GetString() ?? string.Empty
                : string.Empty;

            var missing = doc.RootElement.TryGetProperty("missingHourPrompts", out var mp) &&
                          mp.ValueKind == JsonValueKind.Array
                ? mp.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                    .Where(x => x.Length > 0).ToList()
                : new List<string>();

            return new AiTimesheetResult(entries, summary, missing);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response, using fallback generator.");
            return BuildFallbackResult(weekActivities, weekStart, weekEnd);
        }
    }

    public async Task<string> AnswerChatQueryAsync(
        Guid userId, string question, List<Activity> relevantActivities, CancellationToken ct = default)
    {
        var context = string.Join("\n", relevantActivities
            .OrderByDescending(a => a.ActivityDate)
            .Take(MaxActivitiesInPrompt)
            .Select(a => $"- [{a.ActivityDate:yyyy-MM-dd}] ({a.Source}) {a.Title}" +
                         $"{(a.Status is null ? "" : $" [{a.Status}]")}"));

        var prompt = $"""
            You are an assistant that answers questions about an employee's work activity log.
            Answer only from the log below. If the log does not contain the answer, say so.

            Activity log:
            {context}

            Question: {question}

            Answer concisely, referencing ticket IDs, commit counts or meeting names where relevant.
            """;

        var response = await CallModelAsync(prompt, ct, expectJson: false);
        return response ?? FallbackChatAnswer(relevantActivities);
    }

    private static double ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    private static string BuildPrompt(List<Activity> activities, DateOnly weekStart, DateOnly weekEnd)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Based on these commits, tickets, pull requests and meetings, generate a professional employee timesheet.");
        sb.AppendLine($"Week: {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}");
        sb.AppendLine("Raw activity:");

        foreach (var a in activities.OrderBy(a => a.ActivityDate).Take(MaxActivitiesInPrompt))
        {
            sb.AppendLine(
                $"- [{a.ActivityDate:yyyy-MM-dd}] ({a.Source}) {a.Title} " +
                $"{(a.EstimatedHours.HasValue ? $"~{a.EstimatedHours}h" : string.Empty)}");
        }

        if (activities.Count > MaxActivitiesInPrompt)
        {
            sb.AppendLine($"- … and {activities.Count - MaxActivitiesInPrompt} further items omitted.");
        }

        sb.AppendLine();
        sb.AppendLine("""
            Respond ONLY with strict JSON, no markdown, in this shape:
            {
              "entries": [
                { "date": "YYYY-MM-DD", "description": "...", "hours": 8, "devHours": 5, "meetingHours": 2, "reviewHours": 1 }
              ],
              "weeklySummary": "...",
              "missingHourPrompts": ["..."]
            }
            Combine related raw items into fluent professional sentences (e.g. 'Implemented authentication
            middleware and resolved JWT token validation issues. Participated in Sprint Planning.').
            If a day has less than 6 hours of detected activity, add a missingHourPrompts entry asking about
            documentation, research or learning for that day.
            """);

        return sb.ToString();
    }

    private async Task<string?> CallModelAsync(string prompt, CancellationToken ct, bool expectJson = true)
    {
        var endpoint = _config["AzureOpenAI:Endpoint"];
        var apiKey = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:Deployment"] ?? "gpt-4o";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Azure OpenAI is not configured — using the deterministic generator.");
            return null;
        }

        try
        {
            var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version=2024-06-01";

            var body = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You generate accurate, professional employee timesheets from raw activity data." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                response_format = expectJson ? new { type = "json_object" } : null
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("api-key", apiKey);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure OpenAI call failed with status {Status}.", response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI call threw an exception, falling back.");
            return null;
        }
    }

    /// <summary>
    /// Deterministic generator used whenever Azure OpenAI is unavailable. Always emits a row
    /// for every working day of the week — an empty day is something the employee needs to
    /// fill in, not a row to hide.
    /// </summary>
    internal static AiTimesheetResult BuildFallbackResult(
        List<Activity> activities, DateOnly weekStart, DateOnly weekEnd)
    {
        var byDay = activities
            .GroupBy(a => DateOnly.FromDateTime(a.ActivityDate))
            .ToDictionary(g => g.Key, g => g.ToList());

        var entries = new List<AiGeneratedEntry>();
        var missing = new List<string>();
        var allTitles = new List<string>();

        for (var day = weekStart; day <= weekEnd; day = day.AddDays(1))
        {
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var dayActivities = byDay.TryGetValue(day, out var found) ? found : new List<Activity>();

            // Skip a weekend day only when nothing actually happened on it.
            if (isWeekend && dayActivities.Count == 0) continue;

            var dev = dayActivities.Where(a => DevelopmentSources.Contains(a.Source)).ToList();
            var meetings = dayActivities.Where(a => a.Source == ActivitySource.Meeting).ToList();
            var reviews = dayActivities.Where(a => a.Source == ActivitySource.CodeReview).ToList();

            var devHours = Math.Round(dev.Sum(a => a.EstimatedHours ?? 1), 2);
            var meetingHours = Math.Round(meetings.Sum(a => a.EstimatedHours ?? 0.5), 2);
            var reviewHours = Math.Round(reviews.Sum(a => a.EstimatedHours ?? 0.5), 2);
            var totalHours = Math.Round(devHours + meetingHours + reviewHours, 2);

            var descriptionParts = new List<string>();
            if (dev.Count > 0)
            {
                descriptionParts.Add($"Worked on {string.Join(", ", dev.Select(d => d.Title).Distinct().Take(3))}.");
            }
            if (meetings.Count > 0)
            {
                descriptionParts.Add($"Attended {string.Join(", ", meetings.Select(m => m.Title).Distinct())}.");
            }
            if (reviews.Count > 0)
            {
                descriptionParts.Add($"Reviewed {reviews.Count} pull request{(reviews.Count == 1 ? "" : "s")}.");
            }

            allTitles.AddRange(dev.Select(d => d.Title));

            entries.Add(new AiGeneratedEntry(
                day,
                descriptionParts.Count > 0
                    ? string.Join(" ", descriptionParts)
                    : "No activity detected — add what you worked on.",
                totalHours,
                devHours,
                meetingHours,
                reviewHours));

            if (!isWeekend && totalHours < ExpectedHoursPerDay)
            {
                missing.Add(
                    $"Only {totalHours}h of activity was detected on {day:dddd d MMM}. " +
                    "Did you spend time on documentation, research or learning?");
            }
        }

        var distinctTitles = allTitles.Distinct().ToList();
        var summary = distinctTitles.Count > 0
            ? $"This week you worked on {distinctTitles.Count} key items including " +
              $"{string.Join(", ", distinctTitles.Take(4))}, attended team meetings, and contributed to ongoing sprint goals."
            : "No significant activity was detected for this week.";

        return new AiTimesheetResult(entries, summary, missing);
    }

    private static string FallbackChatAnswer(List<Activity> activities)
    {
        if (activities.Count == 0)
        {
            return "I couldn't find any recorded activity matching that question.";
        }

        var summary = string.Join(", ", activities.Select(a => a.Title).Distinct().Take(5));
        return $"Based on your activity log, you worked on: {summary}.";
    }
}
