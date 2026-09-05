using AITimesheet.TimesheetService.DTOs;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AITimesheet.TimesheetService.Controllers;

[Route("api/chat")]
public class ChatController : ApiControllerBase
{
    private readonly IActivityRepository _activityRepo;
    private readonly IAiTimesheetService _ai;

    public ChatController(IActivityRepository activityRepo, IAiTimesheetService ai)
    {
        _activityRepo = activityRepo;
        _ai = ai;
    }

    /// <summary>
    /// Answers a question about the caller's own activity log. The request body no longer
    /// carries a user id, so one employee can no longer query another's history.
    /// </summary>
    [HttpPost("ask")]
    [EnableRateLimiting("ai")]
    public async Task<ActionResult<ChatResponse>> Ask([FromBody] ChatRequest request, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var activities = await _activityRepo.GetForUserAsync(CurrentUserId, since, null, ct);

        var answer = await _ai.AnswerChatQueryAsync(CurrentUserId, request.Question, activities, ct);
        return Ok(new ChatResponse(answer));
    }
}
