using AITimesheet.TimesheetService.Entities;

namespace AITimesheet.TimesheetService.ServiceLayer.Implementations;

/// <summary>
/// Sample activity for a user who has connected nothing, so the demo still shows a full
/// week.
///
/// Previously this came from every integration's private Mock* method at once: the Jira
/// and Azure DevOps mocks both emitted JiraTicket rows, so a fresh user was handed five
/// overlapping "tickets" from two providers plus duplicate pull requests, and the week's
/// hours were inflated before anyone edited anything. This is one coherent week instead.
/// </summary>
public static class DemoActivityFactory
{
    public static List<Activity> BuildWeek(Guid userId, DateOnly weekStart)
    {
        DateTime On(int dayOffset, int hour) =>
            weekStart.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

        return new List<Activity>
        {
            // Monday
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Sprint Planning", ActivityDate = On(0, 9), EstimatedHours = 1 },
            new() { UserId = userId, Source = ActivitySource.JiraTicket, Title = "ABC-121 Authentication hardening", Status = "In Progress", ExternalReference = "ABC-121", ActivityDate = On(0, 11), EstimatedHours = 3 },
            new() { UserId = userId, Source = ActivitySource.GitCommit, Title = "Fix login authentication issue", ExternalReference = "a1b2c3d", ActivityDate = On(0, 15), EstimatedHours = 2 },

            // Tuesday
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Daily Standup", ActivityDate = On(1, 9), EstimatedHours = 0.25 },
            new() { UserId = userId, Source = ActivitySource.GitCommit, Title = "Add API request validation", ExternalReference = "e4f5g6h", ActivityDate = On(1, 12), EstimatedHours = 2.5 },
            new() { UserId = userId, Source = ActivitySource.WorkItem, Title = "Work item 4821 — Payment API contract", Status = "Active", ExternalReference = "4821", ActivityDate = On(1, 16), EstimatedHours = 3 },

            // Wednesday
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Daily Standup", ActivityDate = On(2, 9), EstimatedHours = 0.25 },
            new() { UserId = userId, Source = ActivitySource.PullRequest, Title = "PR #142: Add API validation middleware", Status = "Open", ExternalReference = "142", ActivityDate = On(2, 11), EstimatedHours = 2 },
            new() { UserId = userId, Source = ActivitySource.CodeReview, Title = "Reviewed PR #139: Dashboard layout", ExternalReference = "139", ActivityDate = On(2, 14), EstimatedHours = 1 },
            new() { UserId = userId, Source = ActivitySource.GitCommit, Title = "Improve dashboard loading states", ExternalReference = "i7j8k9l", ActivityDate = On(2, 16), EstimatedHours = 2.5 },

            // Thursday
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Client Discussion", ActivityDate = On(3, 10), EstimatedHours = 1 },
            new() { UserId = userId, Source = ActivitySource.JiraTicket, Title = "ABC-122 Dashboard filters", Status = "Done", ExternalReference = "ABC-122", ActivityDate = On(3, 12), EstimatedHours = 4 },
            new() { UserId = userId, Source = ActivitySource.GitCommit, Title = "Add filter persistence to dashboard", ExternalReference = "m0n1o2p", ActivityDate = On(3, 16), EstimatedHours = 2 },

            // Friday
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Daily Standup", ActivityDate = On(4, 9), EstimatedHours = 0.25 },
            new() { UserId = userId, Source = ActivitySource.CodeReview, Title = "Reviewed PR #144: Token refresh", ExternalReference = "144", ActivityDate = On(4, 11), EstimatedHours = 1.5 },
            new() { UserId = userId, Source = ActivitySource.Meeting, Title = "Sprint Retrospective", ActivityDate = On(4, 15), EstimatedHours = 0.5 },
            new() { UserId = userId, Source = ActivitySource.GitCommit, Title = "Refactor token refresh handling", ExternalReference = "q3r4s5t", ActivityDate = On(4, 16), EstimatedHours = 3 }
        };
    }
}
