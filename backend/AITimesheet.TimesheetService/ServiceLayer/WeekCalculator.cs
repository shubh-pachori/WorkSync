namespace AITimesheet.TimesheetService;

/// <summary>
/// One authoritative definition of "a week" for the whole system: Monday to Sunday.
///
/// The client used to decide this, and got it wrong — it computed Monday in local time
/// and then serialised with toISOString(), so anyone east of UTC generating a timesheet
/// early in the morning landed on the previous Sunday and created a second timesheet for
/// the same week. The server now snaps whatever date it receives to that week's Monday.
/// </summary>
public static class WeekCalculator
{
    public static DateOnly SnapToWeekStart(DateOnly anyDayInWeek)
    {
        // DayOfWeek is Sunday = 0; shift so Monday = 0.
        var offset = ((int)anyDayInWeek.DayOfWeek + 6) % 7;
        return anyDayInWeek.AddDays(-offset);
    }

    public static DateOnly WeekEndFor(DateOnly weekStart) => SnapToWeekStart(weekStart).AddDays(6);

    public static DateOnly CurrentWeekStart() =>
        SnapToWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
}
