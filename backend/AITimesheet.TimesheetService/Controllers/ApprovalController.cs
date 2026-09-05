using AITimesheet.TimesheetService.DTOs;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AITimesheet.TimesheetService.Controllers;

/// <summary>
/// Manager-only. Previously every action here was reachable by any authenticated user
/// with only a timesheet id, which let an employee approve their own timesheet.
/// </summary>
[Route("api/approvals")]
[Authorize(Roles = Roles.ManagerOrAdmin)]
public class ApprovalController : ApiControllerBase
{
    private readonly ITimesheetRepository _repo;
    private readonly IdentityServiceClient _identityClient;
    private readonly ILogger<ApprovalController> _logger;

    public ApprovalController(
        ITimesheetRepository repo,
        IdentityServiceClient identityClient,
        ILogger<ApprovalController> logger)
    {
        _repo = repo;
        _identityClient = identityClient;
        _logger = logger;
    }

    /// <summary>Timesheets awaiting the calling manager's decision.</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<List<TimesheetDto>>> GetPending(CancellationToken ct)
    {
        var team = await _identityClient.GetEmployeesByManagerIdAsync(CurrentUserId, ct);
        var memberIds = team.Select(u => u.Id).ToList();

        if (memberIds.Count == 0) return Ok(new List<TimesheetDto>());

        var sheets = await _repo.GetPendingApprovalsAsync(memberIds, ct);
        var names = team.ToDictionary(u => u.Id, u => u.FullName);

        return Ok(sheets.Select(t => ToDto(t, names)).ToList());
    }

    /// <summary>
    /// Legacy route kept so existing links do not break. The id in the URL is ignored
    /// unless it matches the caller — a manager can only ever see their own queue.
    /// </summary>
    [HttpGet("pending/{managerId:guid}")]
    public async Task<ActionResult<List<TimesheetDto>>> GetPendingForManager(Guid managerId, CancellationToken ct)
    {
        if (managerId != CurrentUserId)
        {
            return Denied("You may only view your own approval queue.");
        }

        return await GetPending(ct);
    }

    [HttpPost("{timesheetId:guid}/decision")]
    public async Task<IActionResult> Decide(
        Guid timesheetId, [FromBody] ApprovalDecisionRequest request, CancellationToken ct)
    {
        var sheet = await _repo.GetByIdAsync(timesheetId, ct);
        if (sheet?.Approval is null) return NotFound();

        // The decisive check: the sheet's owner must actually report to this manager.
        var employee = await _identityClient.GetUserByIdAsync(sheet.UserId, ct);
        if (employee is null)
        {
            _logger.LogWarning(
                "Timesheet {TimesheetId} references unknown user {UserId}.", timesheetId, sheet.UserId);
            return NotFound();
        }

        if (employee.ManagerId != CurrentUserId)
        {
            _logger.LogWarning(
                "Manager {ManagerId} attempted to decide timesheet {TimesheetId} belonging to {UserId}, " +
                "who reports to {ActualManagerId}.",
                CurrentUserId, timesheetId, sheet.UserId, employee.ManagerId);
            return Denied("This timesheet belongs to someone outside your team.");
        }

        // Only a submitted sheet is awaiting a decision.
        if (sheet.Status != TimesheetStatus.Submitted)
        {
            return Problem(
                title: "Not awaiting approval",
                detail: $"This timesheet is '{sheet.Status}', not 'Submitted'.",
                statusCode: StatusCodes.Status409Conflict);
        }

        sheet.Approval.Status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        sheet.Approval.Comments = request.Comments;
        sheet.Approval.DecidedAt = DateTime.UtcNow;
        sheet.Approval.ManagerId = CurrentUserId;
        sheet.Status = request.Approve ? TimesheetStatus.Approved : TimesheetStatus.Rejected;

        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Manager {ManagerId} {Decision} timesheet {TimesheetId}.",
            CurrentUserId, request.Approve ? "approved" : "rejected", timesheetId);

        return NoContent();
    }

    private static TimesheetDto ToDto(Timesheet t, IReadOnlyDictionary<Guid, string> names)
    {
        var entries = t.Entries
            .OrderBy(e => e.EntryDate)
            .Select(e => new TimesheetEntryDto(
                e.Id, e.EntryDate, e.ActivityDescription, e.Hours,
                e.DevelopmentHours, e.MeetingHours, e.ReviewHours, e.IsEdited))
            .ToList();

        var summary = names.TryGetValue(t.UserId, out var name)
            ? $"{name} — {t.AiWeeklySummary}"
            : t.AiWeeklySummary;

        return new TimesheetDto(
            t.Id, t.UserId, t.WeekStartDate, t.WeekEndDate, t.Status.ToString(),
            summary, entries, t.MissingHourPrompts,
            Math.Round(entries.Sum(e => e.Hours), 2));
    }
}
