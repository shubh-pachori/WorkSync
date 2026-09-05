using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace AITimesheet.TimesheetService.ServiceLayer.Clients;

public record UserDto(Guid Id, string FullName, string Email, string Role, Guid? ManagerId);

/// <summary>
/// Reads the user/manager graph from the identity service.
///
/// Every call authenticates with the shared internal API key, because those endpoints are
/// blocked at the gateway and are not reachable with a user's bearer token. Results are
/// cached briefly: a reporting line changes rarely, and both the approvals and analytics
/// screens hit this on every request.
/// </summary>
public class IdentityServiceClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IdentityServiceClient> _logger;

    public IdentityServiceClient(
        HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<IdentityServiceClient> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;

        var baseUrl = config["Services:IdentityServiceUrl"] ?? "http://localhost:5081/";
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(10);

        var apiKey = config["Internal:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Internal:ApiKey is not configured. The timesheet service cannot call the identity " +
                "service without it. Run scripts/set-dev-secrets.sh or set Internal__ApiKey.");
        }

        _http.DefaultRequestHeaders.Add("X-Internal-Api-Key", apiKey);
    }

    public async Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue<UserDto>(UserKey(userId), out var cached)) return cached;

        try
        {
            var user = await _http.GetFromJsonAsync<UserDto>($"api/auth/internal/users/{userId}", ct);
            if (user is not null) _cache.Set(UserKey(userId), user, CacheDuration);
            return user;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Logged rather than swallowed: an authorization decision that depends on this
            // result must fail closed, and the operator needs to know why.
            _logger.LogError(ex, "Could not resolve user {UserId} from the identity service.", userId);
            return null;
        }
    }

    public async Task<List<UserDto>> GetEmployeesByManagerIdAsync(Guid managerId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue<List<UserDto>>(TeamKey(managerId), out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var team = await _http.GetFromJsonAsync<List<UserDto>>(
                           $"api/auth/internal/users/manager/{managerId}", ct)
                       ?? new List<UserDto>();

            _cache.Set(TeamKey(managerId), team, CacheDuration);
            return team;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not resolve the team for manager {ManagerId}.", managerId);
            return new List<UserDto>();
        }
    }

    private static string UserKey(Guid id) => $"identity:user:{id}";
    private static string TeamKey(Guid id) => $"identity:team:{id}";
}
