using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AITimesheet.IdentityService.Helpers;

public class JwtService
{
    /// <summary>
    /// Audience for the short-lived token issued between the password step and the TOTP
    /// step. It is deliberately different from the access-token audience so a half-finished
    /// login can never be presented to a resource endpoint as a real credential.
    /// </summary>
    public const string MfaAudienceSuffix = ".Mfa";

    private const int MfaTokenMinutes = 5;

    private readonly byte[] _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public JwtService(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set it via user-secrets (development) or the " +
                "Jwt__Key environment variable (deployment).");
        }

        _key = Encoding.UTF8.GetBytes(key);
        if (_key.Length < 32)
        {
            throw new InvalidOperationException(
                $"Jwt:Key must be at least 32 bytes for HmacSha256; got {_key.Length}.");
        }

        _issuer = configuration["Jwt:Issuer"]
                  ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        _audience = configuration["Jwt:Audience"]
                    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

        _expiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var minutes) && minutes > 0
            ? minutes
            : 15;
    }

    private string MfaAudience => _audience + MfaAudienceSuffix;

    /// <summary>
    /// Issues an access token. Every resource endpoint derives the acting user from these
    /// claims — never from a route or body value.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Guid userId, string email, string role)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_expiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return (Write(claims, _audience, expiresAtUtc), expiresAtUtc);
    }

    /// <summary>
    /// Issues the intermediate token that proves the password step succeeded. It carries no
    /// role claim and a five-minute lifetime.
    /// </summary>
    public string GenerateMfaToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return Write(claims, MfaAudience, DateTime.UtcNow.AddMinutes(MfaTokenMinutes));
    }

    /// <summary>
    /// Validates an MFA token and returns the user it belongs to. Rejects anything issued
    /// for the access-token audience, so a valid access token cannot be replayed here.
    /// </summary>
    public bool TryReadMfaToken(string token, out Guid userId)
    {
        userId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(token)) return false;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _issuer,
            ValidAudience = MfaAudience,
            IssuerSigningKey = new SymmetricSecurityKey(_key),
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
            var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(subject, out userId);
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private string Write(List<Claim> claims, string audience, DateTime expiresAtUtc)
    {
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(_key),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
