using System.Security.Cryptography;
using System.Text;

namespace AITimesheet.IdentityService.Helpers;

public interface ITotpService
{
    /// <summary>A new random shared secret, base32-encoded for authenticator apps.</summary>
    string GenerateSecret();

    /// <summary>The otpauth:// URI an authenticator app scans as a QR code.</summary>
    string BuildOtpAuthUri(string base32Secret, string accountEmail, string issuer);

    /// <summary>
    /// Verifies a code against the secret, allowing one step of clock drift either way.
    /// Returns the matched time step so the caller can reject a replay of the same code.
    /// </summary>
    bool TryValidate(string base32Secret, string code, long? lastUsedStep, out long matchedStep);
}

/// <summary>
/// TOTP as specified in RFC 6238, over HMAC-SHA1 with a 30-second step and 6 digits —
/// what Google Authenticator, Microsoft Authenticator, Authy and 1Password all default to.
///
/// Implemented directly on System.Security.Cryptography so the service takes no extra
/// dependency, and unit-tested against the test vectors in RFC 6238 Appendix B.
/// </summary>
public class TotpService : ITotpService
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>How many steps of clock drift to accept either side of now.</summary>
    private const int DriftSteps = 1;

    private const int SecretBytes = 20; // 160 bits, the RFC 4226 recommendation for SHA-1

    public string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    public string BuildOtpAuthUri(string base32Secret, string accountEmail, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountEmail}");

        return $"otpauth://totp/{label}" +
               $"?secret={base32Secret}" +
               $"&issuer={Uri.EscapeDataString(issuer)}" +
               $"&algorithm=SHA1" +
               $"&digits={Digits}" +
               $"&period={StepSeconds}";
    }

    public bool TryValidate(string base32Secret, string code, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;

        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalised = code.Trim().Replace(" ", string.Empty);
        if (normalised.Length != Digits || !normalised.All(char.IsAsciiDigit))
        {
            return false;
        }

        byte[] secret;
        try
        {
            secret = Base32.Decode(base32Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        if (secret.Length == 0) return false;

        var currentStep = CurrentStep();

        for (var offset = -DriftSteps; offset <= DriftSteps; offset++)
        {
            var step = currentStep + offset;

            // Replay protection: a code is valid for its whole window, but only once.
            if (lastUsedStep.HasValue && step <= lastUsedStep.Value) continue;

            var expected = ComputeCode(secret, step, Digits);

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalised)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    public static long CurrentStep(DateTimeOffset? now = null) =>
        (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / StepSeconds;

    /// <summary>
    /// The HOTP truncation from RFC 4226 section 5.3, applied to a time-based counter.
    /// Exposed for the RFC 6238 test vectors.
    /// </summary>
    internal static string ComputeCode(byte[] secret, long step, int digits)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);

        var hash = HMACSHA1.HashData(secret, counter);

        // Dynamic truncation: the low 4 bits of the last byte pick the offset.
        var offset = hash[^1] & 0x0F;

        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(new string('0', digits));
    }
}
