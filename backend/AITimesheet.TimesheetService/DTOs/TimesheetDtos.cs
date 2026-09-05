using System.ComponentModel.DataAnnotations;

namespace AITimesheet.TimesheetService.DTOs;

// ---- Activity ---------------------------------------------------------------------
public record ActivityDto(Guid Id, string Source, string Title, string? Status, DateOnly ActivityDate, double? EstimatedHours);

// ---- Chat -------------------------------------------------------------------------
// UserId removed: the activity log queried is always the caller's own.
public record ChatRequest
{
    [Required(ErrorMessage = "A question is required.")]
    [StringLength(500, MinimumLength = 2, ErrorMessage = "Question must be between 2 and 500 characters.")]
    public string Question { get; init; } = string.Empty;
}

public record ChatResponse(string Answer);

// ---- Connections ------------------------------------------------------------------
// UserId removed: a caller may only connect providers to their own account.
public record ConnectAccountRequest
{
    [Required(ErrorMessage = "Provider is required.")]
    [StringLength(30)]
    public string Provider { get; init; } = string.Empty;

    [Required(ErrorMessage = "An access token is required.")]
    [StringLength(4096)]
    public string AccessToken { get; init; } = string.Empty;

    [StringLength(4096)]
    public string? RefreshToken { get; init; }

    [StringLength(200)]
    public string? ExternalAccountId { get; init; }
}

public record ConnectionStatusDto(
    string Provider,
    bool IsConnected,
    DateTime? ConnectedAt,
    string? LastError);

// ---- Timesheets -------------------------------------------------------------------
public record TimesheetEntryDto(Guid Id, DateOnly Date, string Description, double Hours, double DevHours, double MeetingHours, double ReviewHours, bool IsEdited);

public record TimesheetDto(
    Guid Id,
    Guid UserId,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string Status,
    string? WeeklySummary,
    List<TimesheetEntryDto> Entries,
    // Nudges for days with little detected activity. Previously computed and discarded.
    List<string> MissingHourPrompts,
    double TotalHours);

// UserId removed: a caller may only generate their own timesheet.
public record GenerateTimesheetRequest
{
    /// <summary>
    /// Any date within the target week. The server snaps it to that week's Monday, so a
    /// client timezone quirk cannot silently create a second timesheet for one week.
    /// </summary>
    [Required]
    public DateOnly WeekStartDate { get; init; }
}

public record UpdateEntryRequest
{
    [Range(0, 24, ErrorMessage = "Hours must be between 0 and 24.")]
    public double Hours { get; init; }

    [Required(ErrorMessage = "A description is required.")]
    [StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;
}

public record ApprovalDecisionRequest
{
    [Required]
    public bool Approve { get; init; }

    [StringLength(1000)]
    public string? Comments { get; init; }
}
