using Microsoft.AspNetCore.DataProtection;

namespace AITimesheet.IdentityService.Helpers;

/// <summary>
/// Encrypts the TOTP shared secret before it is written to the database. The secret is
/// password-equivalent — anyone holding it can mint valid codes forever — so it must not
/// sit in the clear in a table.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string? Unprotect(string? protectedValue);
}

public class SecretProtector : ISecretProtector
{
    private const string Purpose = "AITimesheet.TotpSecret.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger)
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
            // Most likely a key-ring rotation. Treated as "no usable secret", which forces
            // the user to re-enrol rather than crashing every login attempt.
            _logger.LogError(ex, "Could not unprotect a stored TOTP secret.");
            return null;
        }
    }
}
