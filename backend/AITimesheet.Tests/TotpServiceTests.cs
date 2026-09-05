using System.Text;
using AITimesheet.IdentityService.Helpers;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// TOTP is verified against the published RFC 6238 test vectors rather than against
/// itself — an implementation that is merely self-consistent would still be rejected by
/// every real authenticator app.
/// </summary>
public class TotpServiceTests
{
    private readonly TotpService _totp = new();

    /// <summary>The seed used throughout RFC 6238 Appendix B.</summary>
    private static byte[] RfcSeed => Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_the_RFC_6238_test_vectors(long unixTime, string expected)
    {
        var step = unixTime / TotpService.StepSeconds;
        Assert.Equal(expected, TotpService.ComputeCode(RfcSeed, step, 8));
    }

    [Fact]
    public void Generated_secrets_are_160_bits_and_unique()
    {
        var secret = _totp.GenerateSecret();

        Assert.Equal(20, Base32.Decode(secret).Length);
        Assert.NotEqual(secret, _totp.GenerateSecret());
    }

    [Fact]
    public void Accepts_the_current_code()
    {
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep();

        Assert.True(_totp.TryValidate(secret, CodeFor(secret, step), null, out var matched));
        Assert.Equal(step, matched);
    }

    [Theory]
    [InlineData(-1)] // the user's phone is a little behind
    [InlineData(1)]  // or a little ahead
    public void Tolerates_one_step_of_clock_drift(int offset)
    {
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep() + offset;

        Assert.True(_totp.TryValidate(secret, CodeFor(secret, step), null, out _));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void Rejects_codes_outside_the_drift_window(int offset)
    {
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep() + offset;

        Assert.False(_totp.TryValidate(secret, CodeFor(secret, step), null, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("12345")]    // too short
    [InlineData("1234567")]  // too long
    [InlineData("12a456")]   // not numeric
    public void Rejects_malformed_or_wrong_codes(string code)
    {
        Assert.False(_totp.TryValidate(_totp.GenerateSecret(), code, null, out _));
    }

    [Fact]
    public void Accepts_a_code_the_user_typed_with_a_space()
    {
        var secret = _totp.GenerateSecret();
        var code = CodeFor(secret, TotpService.CurrentStep());

        Assert.True(_totp.TryValidate(secret, $"{code[..3]} {code[3..]}", null, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!not-base32!!!")]
    public void Rejects_an_unusable_secret_without_throwing(string secret)
    {
        Assert.False(_totp.TryValidate(secret, "123456", null, out _));
    }

    [Fact]
    public void Will_not_accept_the_same_code_twice()
    {
        // A code is valid for its whole 30-second window. Without the last-used step, an
        // attacker who observed one over the user's shoulder could replay it.
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep();
        var code = CodeFor(secret, step);

        Assert.True(_totp.TryValidate(secret, code, null, out var matched));
        Assert.False(_totp.TryValidate(secret, code, matched, out _));
    }

    [Fact]
    public void Will_not_accept_an_older_code_after_a_newer_one()
    {
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep();

        Assert.False(_totp.TryValidate(secret, CodeFor(secret, step - 1), step, out _));
    }

    [Fact]
    public void Still_accepts_the_next_window_after_one_is_consumed()
    {
        var secret = _totp.GenerateSecret();
        var step = TotpService.CurrentStep();

        Assert.True(_totp.TryValidate(secret, CodeFor(secret, step + 1), step, out var matched));
        Assert.Equal(step + 1, matched);
    }

    [Fact]
    public void Builds_an_otpauth_uri_authenticator_apps_can_parse()
    {
        var secret = _totp.GenerateSecret();
        var uri = _totp.BuildOtpAuthUri(secret, "priya@company.com", "AI Timesheet");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("issuer=AI%20Timesheet", uri);
        Assert.Contains("AI%20Timesheet%3Apriya%40company.com", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    private static string CodeFor(string base32Secret, long step) =>
        TotpService.ComputeCode(Base32.Decode(base32Secret), step, TotpService.Digits);
}
