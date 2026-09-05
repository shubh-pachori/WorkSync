namespace AITimesheet.TimesheetService.ServiceLayer.Interfaces;

/// <summary>
/// Encrypts provider access tokens before they touch the database and decrypts them on
/// the way out. Backed by ASP.NET Core Data Protection, so it needs no extra package.
/// </summary>
public interface ITokenProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// Returns the plaintext token, or null when the value cannot be unprotected (for
    /// example a row written before encryption was introduced, or after a key rotation).
    /// </summary>
    string? Unprotect(string? protectedValue);
}
