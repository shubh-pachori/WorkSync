using System.Text;
using AITimesheet.IdentityService.Helpers;
using Xunit;

namespace AITimesheet.Tests;

public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Matches_the_RFC_4648_test_vectors(string plain, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));
        Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded)));
    }

    [Theory]
    [InlineData("MZXW6YTBOI")]
    [InlineData("mzxw6ytboi")]        // authenticator apps show it uppercase; users may not
    [InlineData("MZXW 6YTB OI")]      // as printed with spaces
    [InlineData("MZXW-6YTB-OI")]
    [InlineData("MZXW6YTBOI======")]  // with padding
    public void Decoding_is_forgiving_about_how_a_user_types_it(string input)
    {
        Assert.Equal("foobar", Encoding.ASCII.GetString(Base32.Decode(input)));
    }

    [Fact]
    public void Rejects_characters_outside_the_alphabet()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW1!"));
    }

    [Fact]
    public void Round_trips_random_secrets()
    {
        var secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        Assert.Equal(secret, Base32.Decode(Base32.Encode(secret)));
    }
}
