using AITimesheet.TimesheetService.ServiceLayer.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

public class TokenProtector : ITokenProtector
{
    private const string Purpose = "AITimesheet.ProviderAccessToken.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<TokenProtector> _logger;

    public TokenProtector(IDataProtectionProvider provider, ILogger<TokenProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string plaintext) =>
        string.IsNullOrEmpty(plaintext) ? string.Empty : _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (Exception ex)
        {
            // A row encrypted with a key we no longer hold, or a legacy plaintext row.
            // Treat it as "no usable credential" rather than crashing the request.
            _logger.LogWarning(ex, "Could not unprotect a stored provider token; treating it as unset.");
            return null;
        }
    }
}
