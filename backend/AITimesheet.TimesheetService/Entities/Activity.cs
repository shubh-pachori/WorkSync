using System.ComponentModel.DataAnnotations.Schema;

namespace AITimesheet.TimesheetService.Entities;

public enum ActivitySource
{
    GitCommit,
    PullRequest,
    JiraTicket,
    Meeting,
    CodeReview,

    /// <summary>
    /// Azure DevOps board item. Previously these were recorded as JiraTicket, which made
    /// Azure DevOps and Jira data indistinguishable downstream.
    /// </summary>
    WorkItem
}

public class Activity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ActivitySource Source { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ExternalReference { get; set; } // commit SHA, ABC-123, PR#
    public string? Status { get; set; }
    public DateTime ActivityDate { get; set; }
    public double? EstimatedHours { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identity used to collapse the same item arriving twice — e.g. a work item returned
    /// by both the Jira and Azure DevOps connectors, or a regenerate re-fetching a week.
    /// </summary>
    [NotMapped]
    public (ActivitySource, string, DateOnly) DedupeKey =>
        (Source,
         (ExternalReference ?? Title).Trim().ToLowerInvariant(),
         DateOnly.FromDateTime(ActivityDate));
}
