using AITimesheet.TimesheetService;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// Regression tests for the week-boundary defect: the client computed Monday in local
/// time and serialised it as UTC, so anyone east of UTC generating a timesheet before
/// ~05:30 local landed on the previous Sunday and created a second sheet for one week.
/// </summary>
public class WeekCalculatorTests
{
    [Theory]
    [InlineData("2026-08-31", "2026-08-31")] // Monday -> itself
    [InlineData("2026-09-01", "2026-08-31")] // Tuesday
    [InlineData("2026-09-02", "2026-08-31")] // Wednesday
    [InlineData("2026-09-05", "2026-08-31")] // Saturday
    [InlineData("2026-09-06", "2026-08-31")] // Sunday belongs to the week that started Monday
    [InlineData("2026-08-30", "2026-08-24")] // the value the old client produced
    [InlineData("2026-03-01", "2026-02-23")] // month boundary
    [InlineData("2026-01-01", "2025-12-29")] // year boundary
    public void SnapToWeekStart_returns_the_monday_of_that_week(string input, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), WeekCalculator.SnapToWeekStart(DateOnly.Parse(input)));
    }

    [Fact]
    public void SnapToWeekStart_is_idempotent()
    {
        var once = WeekCalculator.SnapToWeekStart(new DateOnly(2026, 9, 4));
        Assert.Equal(once, WeekCalculator.SnapToWeekStart(once));
    }

    [Fact]
    public void WeekEnd_is_the_sunday_six_days_later()
    {
        Assert.Equal(new DateOnly(2026, 9, 6), WeekCalculator.WeekEndFor(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void WeekEnd_snaps_a_mid_week_input_first()
    {
        Assert.Equal(new DateOnly(2026, 9, 6), WeekCalculator.WeekEndFor(new DateOnly(2026, 9, 2)));
    }

    [Fact]
    public void CurrentWeekStart_is_always_a_monday()
    {
        Assert.Equal(DayOfWeek.Monday, WeekCalculator.CurrentWeekStart().DayOfWeek);
    }
}
