namespace AITimesheet.TimesheetService.Entities;

public enum ConnectionProvider
{
    GitHub,
    AzureDevOps,
    Jira,
    OutlookCalendar,
    TeamsCalendar
}

public class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ConnectionProvider Provider { get; set; }

    /// <summary>Encrypted at rest by ITokenProtector — never a raw provider token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Encrypted at rest by ITokenProtector.</summary>
    public string? RefreshToken { get; set; }

    public string? ExternalAccountId { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Why the last fetch from this provider failed, if it did. Previously every failure
    /// was swallowed and silently replaced with mock data, so a bad token looked identical
    /// to a working connection.
    /// </summary>
    public string? LastError { get; set; }

    public DateTime? LastSyncedAt { get; set; }
}
