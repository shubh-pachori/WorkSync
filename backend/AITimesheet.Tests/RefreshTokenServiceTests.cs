using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using AITimesheet.IdentityService.ServiceLayer.Implementations;
using AITimesheet.IdentityService.ServiceLayer.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AITimesheet.Tests;

/// <summary>
/// Rotation and reuse detection are the whole point of refresh tokens: a stolen token must
/// not grant an attacker a parallel, indefinitely renewable session.
/// </summary>
public class RefreshTokenServiceTests
{
    private static readonly ClientInfo Client = new("127.0.0.1", "tests");
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryRefreshTokenRepository _repo = new();
    private readonly RefreshTokenService _service;

    public RefreshTokenServiceTests()
    {
        _service = new RefreshTokenService(_repo, NullLogger<RefreshTokenService>.Instance);
    }

    [Fact]
    public async Task Issued_tokens_are_unique_and_never_stored_in_the_clear()
    {
        var first = await _service.IssueAsync(UserId, Client);
        var second = await _service.IssueAsync(UserId, Client);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(_repo.Tokens, t => t.TokenHash == first);
        Assert.All(_repo.Tokens, t => Assert.NotEmpty(t.TokenHash));
    }

    [Fact]
    public async Task Issued_tokens_are_url_safe()
    {
        // The value travels in a Set-Cookie header.
        var token = await _service.IssueAsync(UserId, Client);
        Assert.DoesNotContain(token, c => c is '+' or '/' or '=');
    }

    [Fact]
    public async Task Rotation_returns_a_new_token_for_the_same_user()
    {
        var original = await _service.IssueAsync(UserId, Client);

        var result = await _service.RotateAsync(original, Client);

        Assert.NotNull(result);
        Assert.Equal(UserId, result!.UserId);
        Assert.NotEqual(original, result.NewToken);
    }

    [Fact]
    public async Task Rotation_keeps_the_new_token_in_the_same_family()
    {
        var original = await _service.IssueAsync(UserId, Client);
        var result = await _service.RotateAsync(original, Client);

        var families = _repo.Tokens.Select(t => t.FamilyId).Distinct().ToList();

        Assert.NotNull(result);
        Assert.Single(families);
    }

    [Fact]
    public async Task The_old_token_stops_working_after_rotation()
    {
        var original = await _service.IssueAsync(UserId, Client);
        await _service.RotateAsync(original, Client);

        Assert.Null(await _service.RotateAsync(original, Client));
    }

    [Fact]
    public async Task Replaying_a_rotated_token_revokes_the_whole_family()
    {
        // The scenario: an attacker copies the cookie, the real user refreshes first, then
        // the attacker presents the stale token. Both sessions must die, not just one.
        var original = await _service.IssueAsync(UserId, Client);
        var rotated = await _service.RotateAsync(original, Client);
        Assert.NotNull(rotated);

        // The attacker replays the token that was already used.
        Assert.Null(await _service.RotateAsync(original, Client));

        // The legitimate user's current token is now dead too.
        Assert.Null(await _service.RotateAsync(rotated!.NewToken, Client));
        Assert.All(_repo.Tokens, t => Assert.NotNull(t.RevokedAtUtc));
        Assert.Contains(_repo.Tokens, t => t.RevokedReason == RevocationReasons.ReuseDetected);
    }

    [Fact]
    public async Task An_unrelated_session_survives_reuse_detection_in_another_family()
    {
        var laptop = await _service.IssueAsync(UserId, Client);
        var phone = await _service.IssueAsync(UserId, Client);

        await _service.RotateAsync(laptop, Client);
        await _service.RotateAsync(laptop, Client); // reuse on the laptop family

        // The phone logged in separately, so its family is untouched.
        Assert.NotNull(await _service.RotateAsync(phone, Client));
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var token = await _service.IssueAsync(UserId, Client);
        _repo.Tokens.Single(t => t.RevokedAtUtc == null).ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);

        Assert.Null(await _service.RotateAsync(token, Client));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task An_unknown_token_is_rejected(string token)
    {
        await _service.IssueAsync(UserId, Client);
        Assert.Null(await _service.RotateAsync(token, Client));
    }

    [Fact]
    public async Task Signing_out_revokes_the_family()
    {
        var token = await _service.IssueAsync(UserId, Client);

        await _service.RevokeAsync(token, RevocationReasons.SignedOut);

        Assert.Null(await _service.RotateAsync(token, Client));
        Assert.All(_repo.Tokens, t => Assert.Equal(RevocationReasons.SignedOut, t.RevokedReason));
    }

    [Fact]
    public async Task Revoking_a_user_ends_every_session_they_have()
    {
        var laptop = await _service.IssueAsync(UserId, Client);
        var phone = await _service.IssueAsync(UserId, Client);

        await _service.RevokeAllForUserAsync(UserId, RevocationReasons.TotpEnabled);

        Assert.Null(await _service.RotateAsync(laptop, Client));
        Assert.Null(await _service.RotateAsync(phone, Client));
    }

    [Fact]
    public async Task Another_users_sessions_are_left_alone()
    {
        var mine = await _service.IssueAsync(UserId, Client);
        var theirs = await _service.IssueAsync(Guid.NewGuid(), Client);

        await _service.RevokeAllForUserAsync(UserId, RevocationReasons.SignedOut);

        Assert.Null(await _service.RotateAsync(mine, Client));
        Assert.NotNull(await _service.RotateAsync(theirs, Client));
    }

    /// <summary>
    /// A hand-written fake rather than a mocking library: the behaviour under test is the
    /// interaction between rotation and revocation, so the store needs real semantics.
    /// </summary>
    private sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
    {
        public List<RefreshToken> Tokens { get; } = new();

        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct = default)
        {
            foreach (var token in Tokens.Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null))
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                token.RevokedReason = reason;
            }

            return Task.CompletedTask;
        }

        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
        {
            foreach (var token in Tokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null))
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                token.RevokedReason = reason;
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult(Tokens.RemoveAll(t => t.ExpiresAtUtc < olderThanUtc));

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
