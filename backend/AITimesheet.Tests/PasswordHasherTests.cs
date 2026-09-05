using AITimesheet.IdentityService.Helpers;
using Xunit;

namespace AITimesheet.Tests;

public class PasswordHasherTests
{
    // Both seeded demo accounts use this; it is documented in the README.
    private const string DemoPassword = "Demo@123";

    private const string SeededSarahHash =
        "v1.210000.OtcOUci5Tyag0T58W4gkbg==.gG5/8s0AXIhS3m+PHHR0o1v0gQwuIaNxnsGH4Io4Ggs=";

    private const string SeededPriyaHash =
        "v1.210000.nyxBq31eCMMWSpsC3ncxXw==.aWncJ51yUS7BXGfjejJdJguvbAIfuKPhJTH6zBC0GYw=";

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_produces_the_versioned_four_part_format()
    {
        var hash = _hasher.Hash(DemoPassword);

        Assert.Equal(4, hash.Split('.').Length);
        Assert.StartsWith("v1.210000.", hash);
    }

    [Fact]
    public void Hash_uses_a_fresh_salt_each_time()
    {
        Assert.NotEqual(_hasher.Hash(DemoPassword), _hasher.Hash(DemoPassword));
    }

    [Fact]
    public void Verify_accepts_the_correct_password()
    {
        Assert.True(_hasher.Verify(DemoPassword, _hasher.Hash(DemoPassword)));
    }

    [Theory]
    [InlineData("demo@123")]      // wrong case
    [InlineData("Demo@1234")]     // extra character
    [InlineData("")]              // empty
    [InlineData(" Demo@123")]     // leading space
    public void Verify_rejects_anything_else(string candidate)
    {
        Assert.False(_hasher.Verify(candidate, _hasher.Hash(DemoPassword)));
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("v2.210000.AAAA.BBBB")]        // unknown version
    [InlineData("v1.notanumber.AAAA.BBBB")]    // unparseable iteration count
    [InlineData("v1.210000.!!!.???")]          // invalid base64
    [InlineData("v1.210000.AAAA")]             // too few segments
    public void Verify_rejects_a_malformed_stored_hash_without_throwing(string stored)
    {
        Assert.False(_hasher.Verify(DemoPassword, stored));
    }

    [Fact]
    public void Seeded_demo_hashes_match_the_documented_password()
    {
        // Guards the migration seed data against silent drift from the README.
        Assert.True(_hasher.Verify(DemoPassword, SeededSarahHash));
        Assert.True(_hasher.Verify(DemoPassword, SeededPriyaHash));
        Assert.False(_hasher.Verify("wrong", SeededPriyaHash));
    }
}
