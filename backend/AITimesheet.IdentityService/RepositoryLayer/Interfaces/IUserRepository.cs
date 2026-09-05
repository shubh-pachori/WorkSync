using AITimesheet.IdentityService.Entities;

namespace AITimesheet.IdentityService.RepositoryLayer.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the old FindFirstManagerAsync, which looked up the hardcoded address
    /// "manager@company.com" because the repository had no way to query by role.
    /// </summary>
    Task<User?> GetFirstByRoleAsync(string role, CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
