using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AITimesheet.IdentityService.Security;

/// <summary>
/// Guards service-to-service endpoints with a shared secret supplied in the
/// <c>X-Internal-Api-Key</c> header.
///
/// These endpoints are also blocked at the gateway, so this is the second of two
/// layers: even if something is misrouted to the public ingress, the request fails
/// without the key. Fails closed — an unconfigured key rejects every request.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Internal-Api-Key";
    public const string ConfigKey = "Internal:ApiKey";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<InternalApiKeyAttribute>();

        var expected = config[ConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError(
                "{ConfigKey} is not configured — refusing all internal requests. Set it via " +
                "user-secrets or the Internal__ApiKey environment variable.", ConfigKey);
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !CryptoEquals(provided.ToString(), expected))
        {
            logger.LogWarning(
                "Rejected internal request to {Path} with a missing or invalid API key.",
                context.HttpContext.Request.Path);
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }

    private static bool CryptoEquals(string a, string b)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
