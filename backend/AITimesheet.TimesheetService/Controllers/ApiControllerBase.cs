using AITimesheet.TimesheetService.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AITimesheet.TimesheetService.Controllers;

/// <summary>
/// Shared base for every authenticated controller in this service. Exposes the caller's
/// identity from the token so no action has to accept a user id from the client.
/// </summary>
[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();

    protected bool IsManager => User.IsManager();

    /// <summary>403 with an RFC 7807 body, used for ownership and reporting-line failures.</summary>
    protected ObjectResult Denied(string detail) => Problem(
        title: "Forbidden",
        detail: detail,
        statusCode: StatusCodes.Status403Forbidden);
}
