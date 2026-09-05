using AITimesheet.TimesheetService.DTOs;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.RepositoryLayer.Interfaces;
using AITimesheet.TimesheetService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AITimesheet.TimesheetService.Controllers;

/// <summary>
/// Provider connections. Every action operates on the caller's own connections — the
/// user id is never accepted from the request, which previously allowed writing an
/// OAuth token into someone else's account.
/// </summary>
[Route("api/integrations")]
public class IntegrationController : ApiControllerBase
{
    private readonly IConnectionRepository _connectionRepo;
    private readonly ITokenProtector _tokenProtector;
    private readonly ILogger<IntegrationController> _logger;

    public IntegrationController(
        IConnectionRepository connectionRepo,
        ITokenProtector tokenProtector,
        ILogger<IntegrationController> logger)
    {
        _connectionRepo = connectionRepo;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromBody] ConnectAccountRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ConnectionProvider>(request.Provider, ignoreCase: true, out var provider) ||
            !Enum.IsDefined(provider))
        {
            return Problem(
                title: "Unknown provider",
                detail: $"'{request.Provider}' is not one of: {string.Join(", ", Enum.GetNames<ConnectionProvider>())}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await _connectionRepo.GetByUserAndProviderAsync(CurrentUserId, provider, ct);

        // Tokens are encrypted at rest; they were previously stored as plaintext.
        var accessToken = _tokenProtector.Protect(request.AccessToken);
        var refreshToken = request.RefreshToken is null ? null : _tokenProtector.Protect(request.RefreshToken);

        if (existing is not null)
        {
            existing.AccessToken = accessToken;
            existing.RefreshToken = refreshToken;
            existing.ExternalAccountId = request.ExternalAccountId;
            existing.IsActive = true;
            existing.ConnectedAt = DateTime.UtcNow;
            existing.LastError = null;
            await _connectionRepo.UpdateAsync(existing, ct);
        }
        else
        {
            await _connectionRepo.AddAsync(new Connection
            {
                UserId = CurrentUserId,
                Provider = provider,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExternalAccountId = request.ExternalAccountId
            }, ct);
        }

        await _connectionRepo.SaveChangesAsync(ct);
        _logger.LogInformation("User {UserId} connected {Provider}.", CurrentUserId, provider);

        return NoContent();
    }

    [HttpGet("status")]
    public async Task<ActionResult<List<ConnectionStatusDto>>> Status(CancellationToken ct)
    {
        var connections = await _connectionRepo.GetActiveByUserAsync(CurrentUserId, ct);

        var all = Enum.GetValues<ConnectionProvider>()
            .Select(p =>
            {
                var match = connections.FirstOrDefault(c => c.Provider == p);
                return new ConnectionStatusDto(p.ToString(), match is not null, match?.ConnectedAt, match?.LastError);
            })
            .ToList();

        return Ok(all);
    }

    /// <summary>Legacy route; the id must match the caller.</summary>
    [HttpGet("status/{userId:guid}")]
    public async Task<ActionResult<List<ConnectionStatusDto>>> StatusForUser(Guid userId, CancellationToken ct)
    {
        if (userId != CurrentUserId) return Denied("You may only view your own connections.");
        return await Status(ct);
    }

    [HttpDelete("{provider}")]
    public async Task<IActionResult> Disconnect(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<ConnectionProvider>(provider, ignoreCase: true, out var p) || !Enum.IsDefined(p))
        {
            return Problem(
                title: "Unknown provider",
                detail: $"'{provider}' is not a known provider.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var conn = await _connectionRepo.GetByUserAndProviderAsync(CurrentUserId, p, ct);
        if (conn is null) return NotFound();

        conn.IsActive = false;
        // Drop the credential entirely rather than leaving it in a disabled row.
        conn.AccessToken = string.Empty;
        conn.RefreshToken = null;

        await _connectionRepo.UpdateAsync(conn, ct);
        await _connectionRepo.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} disconnected {Provider}.", CurrentUserId, p);
        return NoContent();
    }
}
