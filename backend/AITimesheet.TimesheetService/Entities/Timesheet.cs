using System.ComponentModel.DataAnnotations.Schema;

namespace AITimesheet.TimesheetService.Entities;

public enum TimesheetStatus
{
    Draft,
    Generated,
    Submitted,
    Approved,
    Rejected
}

public class Timesheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly WeekStartDate { get; set; }
    public DateOnly WeekEndDate { get; set; }
    public TimesheetStatus Status { get; set; } = TimesheetStatus.Draft;
    public string? AiWeeklySummary { get; set; }

    /// <summary>
    /// Prompts for days with little detected activity ("only 3.5 hours on Tuesday — did you
    /// work on documentation?"). Persisted as JSON so the review screen can show them.
    /// </summary>
    public List<string> MissingHourPrompts { get; set; } = new();

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    public ICollection<TimesheetEntry> Entries { get; set; } = new List<TimesheetEntry>();
    public Approval? Approval { get; set; }

    /// <summary>A sheet is only editable by its owner before it has been submitted.</summary>
    [NotMapped]
    public bool IsEditable => Status is TimesheetStatus.Draft or TimesheetStatus.Generated;
}
