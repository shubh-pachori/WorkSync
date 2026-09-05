using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Implementations;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// The deterministic generator is what actually runs whenever Azure OpenAI is not
/// configured, which is the default. It is pure, so it is the highest-value unit under
/// test in the whole solution.
/// </summary>
public class FallbackTimesheetGeneratorTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 8, 31);
    private static readonly DateOnly Sunday = new(2026, 9, 6);

    private static Activity Activity(ActivitySource source, int dayOffset, double hours, string title = "work") =>
        new()
        {
            UserId = UserId,
            Source = source,
            Title = title,
            ActivityDate = Monday.AddDays(dayOffset).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EstimatedHours = hours
        };

    [Fact]
    public void Emits_one_row_for_every_working_day()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            DemoActivityFactory.BuildWeek(UserId, Monday), Monday, Sunday);

        Assert.Equal(5, result.Entries.Count);
        Assert.Equal(
            Enumerable.Range(0, 5).Select(i => Monday.AddDays(i)),
            result.Entries.Select(e => e.Date));
    }

    [Fact]
    public void An_empty_week_still_produces_fillable_rows()
    {
        // Previously an empty week produced a timesheet with no rows at all, which the
        // employee had no way to correct.
        var result = OpenAiTimesheetService.BuildFallbackResult(new List<Activity>(), Monday, Sunday);

        Assert.Equal(5, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal(0, e.Hours));
        Assert.Equal(5, result.MissingHourPrompts.Count);
    }

    [Fact]
    public void Hours_split_into_categories_that_sum_to_the_total()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(new List<Activity>
        {
            Activity(ActivitySource.GitCommit, 0, 3),
            Activity(ActivitySource.Meeting, 0, 1),
            Activity(ActivitySource.CodeReview, 0, 1.5)
        }, Monday, Sunday);

        var monday = result.Entries.First(e => e.Date == Monday);

        Assert.Equal(3, monday.DevHours);
        Assert.Equal(1, monday.MeetingHours);
        Assert.Equal(1.5, monday.ReviewHours);
        Assert.Equal(5.5, monday.Hours);
    }

    [Fact]
    public void Azure_devops_work_items_count_as_development()
    {
        // They used to be recorded as JiraTicket, so this categorisation was accidental.
        var result = OpenAiTimesheetService.BuildFallbackResult(
            new List<Activity> { Activity(ActivitySource.WorkItem, 0, 4) }, Monday, Sunday);

        Assert.Equal(4, result.Entries.First(e => e.Date == Monday).DevHours);
    }

    [Fact]
    public void A_light_day_raises_a_missing_hour_prompt()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            new List<Activity> { Activity(ActivitySource.GitCommit, 0, 2) }, Monday, Sunday);

        Assert.Contains(result.MissingHourPrompts, p => p.Contains("Monday"));
    }

    [Fact]
    public void A_full_day_raises_no_prompt()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            new List<Activity> { Activity(ActivitySource.GitCommit, 0, 8) }, Monday, Sunday);

        Assert.DoesNotContain(result.MissingHourPrompts, p => p.Contains("Monday"));
    }

    [Fact]
    public void A_quiet_weekend_is_skipped_entirely()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            DemoActivityFactory.BuildWeek(UserId, Monday), Monday, Sunday);

        Assert.DoesNotContain(result.Entries, e => e.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.DoesNotContain(result.MissingHourPrompts, p => p.Contains("Saturday") || p.Contains("Sunday"));
    }

    [Fact]
    public void A_weekend_with_real_work_is_recorded()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            new List<Activity> { Activity(ActivitySource.GitCommit, 5, 2, "hotfix") }, Monday, Sunday);

        Assert.Contains(result.Entries, e => e.Date == Monday.AddDays(5));
    }

    [Fact]
    public void Summary_names_the_work_that_was_found()
    {
        var result = OpenAiTimesheetService.BuildFallbackResult(
            new List<Activity> { Activity(ActivitySource.JiraTicket, 0, 3, "ABC-42 Payment retries") },
            Monday, Sunday);

        Assert.Contains("ABC-42", result.WeeklySummary);
    }
}
