using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AITimesheet.TimesheetService.Controllers;

/// <summary>
/// Manager-only team reporting. The manager id is taken from the token, so one manager
/// can no longer read another manager's team by changing a route parameter.
/// </summary>
[Route("api/analytics")]
[Authorize(Roles = Roles.ManagerOrAdmin)]
public class AnalyticsController : ApiControllerBase
{
    private readonly ITimesheetRepository _repo;
    private readonly IdentityServiceClient _identityClient;

    public AnalyticsController(ITimesheetRepository repo, IdentityServiceClient identityClient)
    {
        _repo = repo;
        _identityClient = identityClient;
    }

    [HttpGet("team")]
    public async Task<IActionResult> GetTeamAnalytics(CancellationToken ct)
    {
        var teamMembers = await _identityClient.GetEmployeesByManagerIdAsync(CurrentUserId, ct);
        var teamUserIds = teamMembers.Select(u => u.Id).ToList();

        if (teamUserIds.Count == 0)
        {
            return Ok(new
            {
                byStatus = new Dictionary<string, int>(),
                weeklyHours = Array.Empty<object>(),
                perEmployee = Array.Empty<object>()
            });
        }

        var timesheets = await _repo.GetByUsersAsync(teamUserIds, ct);

        var byStatus = timesheets
            .GroupBy(t => t.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        var weeklyHours = timesheets
            .GroupBy(t => t.WeekStartDate)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                week = g.Key,
                totalHours = Math.Round(g.SelectMany(t => t.Entries).Sum(e => e.Hours), 2)
            })
            .ToList();

        var employeeMap = teamMembers.ToDictionary(u => u.Id, u => u.FullName);

        var perEmployee = timesheets
            .GroupBy(t => t.UserId)
            .Select(g => new
            {
                employee = employeeMap.TryGetValue(g.Key, out var name) ? name : "Unknown employee",
                totalHours = Math.Round(g.SelectMany(t => t.Entries).Sum(e => e.Hours), 2),
                submitted = g.Count(t => t.Status != TimesheetStatus.Draft && t.Status != TimesheetStatus.Generated),
                approved = g.Count(t => t.Status == TimesheetStatus.Approved)
            })
            .OrderBy(x => x.employee)
            .ToList();

        return Ok(new { byStatus, weeklyHours, perEmployee });
    }

    /// <summary>Legacy route; the id must match the caller.</summary>
    [HttpGet("team/{managerId:guid}")]
    public async Task<IActionResult> GetTeamAnalyticsForManager(Guid managerId, CancellationToken ct)
    {
        if (managerId != CurrentUserId)
        {
            return Denied("You may only view analytics for your own team.");
        }

        return await GetTeamAnalytics(ct);
    }
}
