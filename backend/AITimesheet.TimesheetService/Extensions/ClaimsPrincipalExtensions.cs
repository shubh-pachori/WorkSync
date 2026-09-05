using System.Security.Claims;
using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The acting user, taken from the validated JWT. This is the ONLY approved source of
    /// caller identity — route parameters and request bodies are attacker-controlled.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(raw, out var userId))
        {
            // [Authorize] has already run, so a token without a usable subject is a bug
            // in the issuer rather than something a client can trigger.
            throw new InvalidOperationException(
                "The authenticated principal has no valid NameIdentifier claim.");
        }

        return userId;
    }

    public static bool IsManager(this ClaimsPrincipal principal) =>
        principal.IsInRole(Roles.Manager) || principal.IsInRole(Roles.Admin);
}
