using AITimesheet.TimesheetService.DTOs;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Clients;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AITimesheet.TimesheetService.Controllers;

[Route("api/timesheets")]
public class TimesheetController : ApiControllerBase
{
    private readonly ITimesheetRepository _repo;
    private readonly ITimesheetGenerationService _generator;
    private readonly IdentityServiceClient _identityClient;

    public TimesheetController(
        ITimesheetRepository repo,
        ITimesheetGenerationService generator,
        IdentityServiceClient identityClient)
    {
        _repo = repo;
        _generator = generator;
        _identityClient = identityClient;
    }

    /// <summary>
    /// Generates (or regenerates) the caller's timesheet for a week. The user id comes
    /// from the token — the request body no longer carries one.
    /// </summary>
    [HttpPost("generate")]
    [EnableRateLimiting("ai")] // fans out to provider APIs and the AI model
    public async Task<ActionResult<TimesheetDto>> Generate(
        [FromBody] GenerateTimesheetRequest request, CancellationToken ct)
    {
        var weekStart = WeekCalculator.SnapToWeekStart(request.WeekStartDate);

        if (weekStart > WeekCalculator.CurrentWeekStart().AddDays(7))
        {
            return Problem(
                title: "Invalid week",
                detail: "A timesheet cannot be generated more than one week ahead.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var timesheet = await _generator.GenerateForWeekAsync(CurrentUserId, weekStart, ct);
        return Ok(ToDto(timesheet));
    }

    /// <summary>The caller's own timesheets. A manager may also read a direct report's.</summary>
    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<List<TimesheetDto>>> GetForUser(Guid userId, CancellationToken ct)
    {
        if (userId != CurrentUserId && !await CanSuperviseAsync(userId, ct))
        {
            return Denied("You may only read your own timesheets, or those of your direct reports.");
        }

        var sheets = await _repo.GetByUserAsync(userId, ct);
        return Ok(sheets.Select(ToDto).ToList());
    }

    /// <summary>Convenience alias that needs no id at all.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<TimesheetDto>>> GetMine(CancellationToken ct)
    {
        var sheets = await _repo.GetByUserAsync(CurrentUserId, ct);
        return Ok(sheets.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TimesheetDto>> GetById(Guid id, CancellationToken ct)
    {
        var sheet = await _repo.GetByIdAsync(id, ct);
        if (sheet is null) return NotFound();

        if (sheet.UserId != CurrentUserId && !await CanSuperviseAsync(sheet.UserId, ct))
        {
            // 404 rather than 403: a stranger's timesheet id should not be confirmable.
            return NotFound();
        }

        return Ok(ToDto(sheet));
    }

    [HttpPut("{timesheetId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> UpdateEntry(
        Guid timesheetId, Guid entryId, [FromBody] UpdateEntryRequest request, CancellationToken ct)
    {
        var sheet = await _repo.GetByIdAsync(timesheetId, ct);
        if (sheet is null) return NotFound();

        // Ownership: only the employee the sheet belongs to may edit it.
        if (sheet.UserId != CurrentUserId) return NotFound();

        // Lifecycle: a submitted or decided sheet is immutable.
        if (!sheet.IsEditable)
        {
            return Problem(
                title: "Timesheet is locked",
                detail: $"A timesheet with status '{sheet.Status}' can no longer be edited.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var entry = sheet.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return NotFound();

        entry.Hours = request.Hours;
        entry.ActivityDescription = request.Description;
        entry.IsEdited = true;

        await _repo.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var sheet = await _repo.GetByIdAsync(id, ct);
        if (sheet is null) return NotFound();
        if (sheet.UserId != CurrentUserId) return NotFound();

        if (!sheet.IsEditable)
        {
            return Problem(
                title: "Already submitted",
                detail: $"This timesheet is already '{sheet.Status}'.",
                statusCode: StatusCodes.Status409Conflict);
        }

        sheet.Status = TimesheetStatus.Submitted;
        sheet.SubmittedAt = DateTime.UtcNow;

        // Reuse the existing approval row on resubmission instead of inserting a second one.
        if (sheet.Approval is null)
        {
            await _repo.AddApprovalAsync(
                new Approval { TimesheetId = sheet.Id, Status = ApprovalStatus.Pending }, ct);
        }
        else
        {
            sheet.Approval.Status = ApprovalStatus.Pending;
            sheet.Approval.Comments = null;
            sheet.Approval.DecidedAt = null;
            sheet.Approval.ManagerId = null;
        }

        await _repo.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>True when the caller is a manager and the given user reports to them.</summary>
    private async Task<bool> CanSuperviseAsync(Guid employeeId, CancellationToken ct)
    {
        if (!IsManager) return false;

        var employee = await _identityClient.GetUserByIdAsync(employeeId, ct);
        return employee?.ManagerId == CurrentUserId;
    }

    private static TimesheetDto ToDto(Timesheet t)
    {
        var entries = t.Entries
            .OrderBy(e => e.EntryDate)
            .Select(e => new TimesheetEntryDto(
                e.Id, e.EntryDate, e.ActivityDescription, e.Hours,
                e.DevelopmentHours, e.MeetingHours, e.ReviewHours, e.IsEdited))
            .ToList();

        return new TimesheetDto(
            t.Id, t.UserId, t.WeekStartDate, t.WeekEndDate, t.Status.ToString(),
            t.AiWeeklySummary, entries, t.MissingHourPrompts,
            Math.Round(entries.Sum(e => e.Hours), 2));
    }
}
