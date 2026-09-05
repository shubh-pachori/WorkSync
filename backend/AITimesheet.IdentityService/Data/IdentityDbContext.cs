using AITimesheet.IdentityService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AITimesheet.IdentityService.Data;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    // ---- Demo seed data -------------------------------------------------------------
    // Fixed GUIDs and a fixed PBKDF2 salt keep HasData deterministic: re-running the
    // migration on another machine produces byte-identical rows. Both demo accounts use
    // the password "Demo@123", which is documented in the README and is expected to be
    // rotated before this is exposed to anything real.
    public static readonly Guid SarahId = Guid.Parse("2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58");
    public static readonly Guid PriyaId = Guid.Parse("8f7d3c1e-1b64-4a2f-9d05-6c1a7e93b420");

    private const string SarahPasswordHash =
        "v1.210000.OtcOUci5Tyag0T58W4gkbg==.gG5/8s0AXIhS3m+PHHR0o1v0gQwuIaNxnsGH4Io4Ggs=";
    private const string PriyaPasswordHash =
        "v1.210000.nyxBq31eCMMWSpsC3ncxXw==.aWncJ51yUS7BXGfjejJdJguvbAIfuKPhJTH6zBC0GYw=";

    private static readonly DateTime SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<AuditLog>().ToTable("audit_logs");
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<RecoveryCode>().ToTable("recovery_codes");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Audit queries are almost always "what did this user do, most recent first".
        modelBuilder.Entity<AuditLog>()
            .HasIndex(l => new { l.UserId, l.Timestamp });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            // Every refresh is a lookup by hash, so this index is on the hot path.
            token.HasIndex(t => t.TokenHash).IsUnique();

            // Reuse detection revokes a whole family at once.
            token.HasIndex(t => t.FamilyId);

            // "Revoke everything for this user" on sign-out and 2FA changes.
            token.HasIndex(t => t.UserId);

            // Expiry sweep.
            token.HasIndex(t => t.ExpiresAtUtc);
        });

        modelBuilder.Entity<RecoveryCode>(code =>
        {
            code.HasIndex(c => new { c.UserId, c.UsedAtUtc });
        });

        // Force UTC on every DateTime column so Npgsql never sees Kind=Unspecified.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcConverter);
                }
            }
        }

        SeedDemoUsers(modelBuilder);
    }

    /// <summary>
    /// Replaces the old "first login invents a manager" behaviour. The reporting line is
    /// explicit here, so Priya actually reports to Sarah and the approval flow works on a
    /// fresh database with no manual setup.
    /// </summary>
    private static void SeedDemoUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = SarahId,
                FullName = "Sarah Jenkins",
                Email = "sarah@company.com",
                PasswordHash = SarahPasswordHash,
                Role = UserRoles.Manager,
                ManagerId = null,
                CreatedAt = SeedTimestamp,
                IsActive = true,
                AzureAdObjectId = null
            },
            new User
            {
                Id = PriyaId,
                FullName = "Priya Sharma",
                Email = "priya@company.com",
                PasswordHash = PriyaPasswordHash,
                Role = UserRoles.Employee,
                ManagerId = SarahId,
                CreatedAt = SeedTimestamp,
                IsActive = true,
                AzureAdObjectId = null
            });
    }
}
