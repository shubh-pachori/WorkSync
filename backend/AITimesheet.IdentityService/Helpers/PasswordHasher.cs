using System.Security.Cryptography;

namespace AITimesheet.IdentityService.Helpers;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing.
///
/// Uses only System.Security.Cryptography from the base framework, so the service
/// takes no extra dependency on Microsoft.Extensions.Identity.Core.
///
/// Encoded format (single column, self-describing so the work factor can be raised
/// later without a schema change):
///
///     v1.{iterations}.{base64 salt}.{base64 derived key}
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

public class PasswordHasher : IPasswordHasher
{
    // OWASP-recommended work factor for PBKDF2-HMAC-SHA256.
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Version = "v1";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"{Version}.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('.');
        if (parts.Length != 4 || parts[0] != Version)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Constant-time comparison — never use SequenceEqual for secrets.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
