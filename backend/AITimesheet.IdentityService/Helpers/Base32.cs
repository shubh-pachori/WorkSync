namespace AITimesheet.IdentityService.Helpers;

/// <summary>
/// RFC 4648 base32 (no padding on encode, tolerant on decode).
///
/// Authenticator apps expect the shared secret in base32 inside the otpauth:// URI,
/// so this is needed to hand the secret to Google Authenticator, Authy, 1Password etc.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return string.Empty;

        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);

        int buffer = 0;
        int bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Alphabet[(buffer >> bitsLeft) & 31]);
            }
        }

        if (bitsLeft > 0)
        {
            result.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return result.ToString();
    }

    public static byte[] Decode(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return Array.Empty<byte>();

        var bytes = new List<byte>(encoded.Length * 5 / 8);

        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in encoded)
        {
            // Ignore padding and the spaces users paste in from a printed secret.
            if (c is '=' or ' ' or '-') continue;

            var value = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0)
            {
                throw new FormatException($"'{c}' is not a valid base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
