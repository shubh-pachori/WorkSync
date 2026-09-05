namespace AITimesheet.TimesheetService.Entities;

/// <summary>
/// Role names as written into the JWT by the identity service. Kept as constants so
/// [Authorize(Roles = ...)] and IsInRole() can never drift apart by a typo.
/// </summary>
public static class Roles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Admin = "Admin";

    /// <summary>Comma-separated form accepted by [Authorize(Roles = ...)].</summary>
    public const string ManagerOrAdmin = Manager + "," + Admin;
}
