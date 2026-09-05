using System.Security.Claims;
using AITimesheet.TimesheetService.Entities;
using AITimesheet.TimesheetService.Extensions;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// The whole authorization model rests on reading identity from the token rather than
/// from a route or body value, so these are worth pinning down.
/// </summary>
public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal PrincipalFor(Guid? userId, string? role)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public void GetUserId_reads_the_subject_from_the_token()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, PrincipalFor(id, Roles.Employee).GetUserId());
    }

    [Fact]
    public void GetUserId_throws_when_the_claim_is_missing()
    {
        Assert.Throws<InvalidOperationException>(() => PrincipalFor(null, Roles.Employee).GetUserId());
    }

    [Fact]
    public void GetUserId_throws_when_the_claim_is_not_a_guid()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") }, "TestAuth"));

        Assert.Throws<InvalidOperationException>(() => principal.GetUserId());
    }

    [Theory]
    [InlineData(Roles.Manager, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Employee, false)]
    public void IsManager_follows_the_role_claim(string role, bool expected)
    {
        Assert.Equal(expected, PrincipalFor(Guid.NewGuid(), role).IsManager());
    }

    [Fact]
    public void IsManager_is_false_without_any_role_claim()
    {
        Assert.False(PrincipalFor(Guid.NewGuid(), null).IsManager());
    }
}
