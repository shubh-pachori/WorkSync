using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.ServiceLayer.Implementations;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// Regression tests for the duplicate-activity defect: generate appended a fresh copy of
/// every commit, ticket and meeting on each run, and the Jira and Azure DevOps demo sets
/// both emitted the same tickets.
/// </summary>
public class ActivityDeduplicationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 8, 31);

    private static DateTime OnDay(int offset) =>
        Monday.AddDays(offset).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static Activity Make(ActivitySource source, string title, string? reference, int dayOffset) =>
        new()
        {
            UserId = UserId,
            Source = source,
            Title = title,
            ExternalReference = reference,
            ActivityDate = OnDay(dayOffset)
        };

    [Fact]
    public void Same_reference_source_and_day_collapses_to_one_row()
    {
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.JiraTicket, "ABC-1 Login bug", "ABC-1", 0),
            Make(ActivitySource.JiraTicket, "ABC-1 Login bug (updated)", "ABC-1", 0)
        });

        Assert.Single(result);
    }

    [Fact]
    public void External_reference_matching_ignores_case_and_padding()
    {
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.JiraTicket, "ABC-1", "ABC-1", 0),
            Make(ActivitySource.JiraTicket, "ABC-1", " abc-1 ", 0)
        });

        Assert.Single(result);
    }

    [Fact]
    public void The_same_reference_from_a_different_provider_is_kept()
    {
        // A Jira issue and an Azure DevOps work item can legitimately share an id.
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.JiraTicket, "ABC-1", "ABC-1", 0),
            Make(ActivitySource.WorkItem, "ABC-1", "ABC-1", 0)
        });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void The_same_item_on_a_different_day_is_kept()
    {
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.JiraTicket, "ABC-1", "ABC-1", 0),
            Make(ActivitySource.JiraTicket, "ABC-1", "ABC-1", 1)
        });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Items_without_a_reference_fall_back_to_the_title()
    {
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.Meeting, "Daily Standup", null, 0),
            Make(ActivitySource.Meeting, "Daily Standup", null, 0),
            Make(ActivitySource.Meeting, "Sprint Planning", null, 0)
        });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Output_is_ordered_by_date()
    {
        var result = TimesheetGenerationService.Deduplicate(new[]
        {
            Make(ActivitySource.GitCommit, "third", "c", 2),
            Make(ActivitySource.GitCommit, "first", "a", 0),
            Make(ActivitySource.GitCommit, "second", "b", 1)
        });

        Assert.Equal(new[] { "first", "second", "third" }, result.Select(a => a.Title));
    }

    [Fact]
    public void The_demo_week_contains_no_duplicates()
    {
        var demo = DemoActivityFactory.BuildWeek(UserId, Monday);
        Assert.Equal(demo.Count, TimesheetGenerationService.Deduplicate(demo).Count);
    }

    [Fact]
    public void The_demo_week_stays_inside_the_requested_week()
    {
        var demo = DemoActivityFactory.BuildWeek(UserId, Monday);

        Assert.All(demo, a =>
        {
            var day = DateOnly.FromDateTime(a.ActivityDate);
            Assert.InRange(day, Monday, Monday.AddDays(6));
        });
    }
}
