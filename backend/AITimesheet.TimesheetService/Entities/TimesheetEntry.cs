namespace AITimesheet.TimesheetService.Entities;

public class TimesheetEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }
    public DateOnly EntryDate { get; set; }
    public string ActivityDescription { get; set; } = string.Empty;
    public double Hours { get; set; }
    public double DevelopmentHours { get; set; }
    public double MeetingHours { get; set; }
    public double ReviewHours { get; set; }
    public bool IsEdited { get; set; } = false;
}
