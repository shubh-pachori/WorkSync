using AITimesheet.TimesheetService.DTOs;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Clients;
using Microsoft.AspNetCore.Mvc;

namespace AITimesheet.TimesheetService.Controllers;

[Route("api/activities")]
public class ActivityController : ApiControllerBase
{
    private readonly IActivityRepository _activityRepo;
    private readonly IdentityServiceClient _identityClient;

    public ActivityController(IActivityRepository activityRepo, IdentityServiceClient identityClient)
    {
        _activityRepo = activityRepo;
        _identityClient = identityClient;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<ActivityDto>>> GetMine(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await LoadAsync(CurrentUserId, from, to, ct));

    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<List<ActivityDto>>> GetForUser(
        Guid userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (userId != CurrentUserId)
        {
            // A manager may inspect a direct report's raw activity; nobody else may.
            if (!IsManager) return Denied("You may only read your own activity.");

            var employee = await _identityClient.GetUserByIdAsync(userId, ct);
            if (employee?.ManagerId != CurrentUserId)
            {
                return Denied("That user is not one of your direct reports.");
            }
        }

        return Ok(await LoadAsync(userId, from, to, ct));
    }

    private async Task<List<ActivityDto>> LoadAsync(
        Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var fromDt = from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var results = await _activityRepo.GetForUserAsync(userId, fromDt, toDt, ct);

        return results
            .Select(a => new ActivityDto(
                a.Id, a.Source.ToString(), a.Title, a.Status,
                DateOnly.FromDateTime(a.ActivityDate), a.EstimatedHours))
            .ToList();
    }
}
