using System.Text.Json;
using AITimesheet.TimesheetService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AITimesheet.TimesheetService.Data;

public class TimesheetDbContext : DbContext
{
    public TimesheetDbContext(DbContextOptions<TimesheetDbContext> options) : base(options) { }

    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Timesheet> Timesheets => Set<Timesheet>();
    public DbSet<TimesheetEntry> TimesheetEntries => Set<TimesheetEntry>();
    public DbSet<Approval> Approvals => Set<Approval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Snake_case table names for Postgres friendliness.
        modelBuilder.Entity<Connection>().ToTable("connections");
        modelBuilder.Entity<Activity>().ToTable("activities");
        modelBuilder.Entity<Timesheet>().ToTable("timesheets");
        modelBuilder.Entity<TimesheetEntry>().ToTable("timesheet_entries");
        modelBuilder.Entity<Approval>().ToTable("approvals");

        modelBuilder.Entity<Connection>(connection =>
        {
            // One row per user per provider. The connect endpoint upserts on this pair,
            // so the database now enforces what the code assumed.
            connection.HasIndex(c => new { c.UserId, c.Provider }).IsUnique();
            connection.Property(c => c.Provider).HasConversion<string>();
        });

        modelBuilder.Entity<Activity>(activity =>
        {
            // Matches the two access patterns: "this user's week" and "this user, recent first".
            activity.HasIndex(a => new { a.UserId, a.ActivityDate });
            activity.Property(a => a.Source).HasConversion<string>();
        });

        modelBuilder.Entity<Timesheet>(timesheet =>
        {
            // The invariant behind the duplicate-week bug: one timesheet per user per week.
            timesheet.HasIndex(t => new { t.UserId, t.WeekStartDate }).IsUnique();
            timesheet.HasIndex(t => t.Status);
            timesheet.Property(t => t.Status).HasConversion<string>();

            // MissingHourPrompts is a small string list; JSON keeps it in one column
            // without a Postgres-specific array mapping.
            var promptsConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            // Without a comparer EF compares the list by reference and misses edits.
            var promptsComparer = new ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                v => v.ToList());

            timesheet.Property(t => t.MissingHourPrompts)
                .HasConversion(promptsConverter, promptsComparer)
                .HasColumnType("text")
                .IsRequired();
        });

        modelBuilder.Entity<TimesheetEntry>()
            .HasOne(e => e.Timesheet)
            .WithMany(t => t.Entries)
            .HasForeignKey(e => e.TimesheetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Approval>(approval =>
        {
            approval.HasOne(a => a.Timesheet)
                .WithOne(t => t.Approval)
                .HasForeignKey<Approval>(a => a.TimesheetId)
                .OnDelete(DeleteBehavior.Cascade);

            approval.Property(a => a.Status).HasConversion<string>();
        });

        // Force UTC on all DateTime columns to make Npgsql happy.
        var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
            }
        }
    }
}
