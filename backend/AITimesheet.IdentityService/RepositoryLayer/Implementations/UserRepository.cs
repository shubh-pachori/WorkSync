using AITimesheet.IdentityService.Data;
using AITimesheet.IdentityService.Entities;
using AITimesheet.IdentityService.RepositoryLayer.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AITimesheet.IdentityService.RepositoryLayer.Implementations;

public class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _db;

    public UserRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Users.FindAsync(new object[] { id }, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<List<User>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default) =>
        await _db.Users
            .AsNoTracking()
            .Where(u => u.ManagerId == managerId && u.IsActive)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

    public async Task<User?> GetFirstByRoleAsync(string role, CancellationToken ct = default) =>
        await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == role && u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _db.Users.Update(user);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
