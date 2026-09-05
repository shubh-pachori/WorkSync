namespace AITimesheet.TimesheetService.Entities;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public class Approval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }
    public Guid? ManagerId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? Comments { get; set; }
    public DateTime? DecidedAt { get; set; }
}
