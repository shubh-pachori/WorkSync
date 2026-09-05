using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AITimesheet.TimesheetService.Migrations
{
    /// <summary>
    /// Adds the constraints and indexes the code always assumed:
    ///  * one timesheet per user per week (the duplicate-week bug had no database guard),
    ///  * one connection row per user per provider,
    ///  * a covering index for the "this user's activity in this range" query,
    ///  * per-connection sync error state, so a failed provider fetch is visible instead of
    ///    being silently replaced with mock data,
    ///  * storage for the missing-hour prompts, which were computed and then discarded.
    ///
    /// Rows that violate the new uniqueness rules are collapsed first, keeping the most
    /// recent of each group, otherwise index creation would fail on an existing database.
    /// </summary>
    public partial class HardenTimesheetSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- New columns ---------------------------------------------------------
            migrationBuilder.AddColumn<string>(
                name: "MissingHourPrompts",
                table: "timesheets",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);

            // ---- Collapse rows that the new unique indexes would reject ---------------
            // Keep the newest timesheet for each (user, week); the older ones are the
            // artefacts of the client-side week-boundary bug.
            migrationBuilder.Sql("""
                DELETE FROM timesheets t
                USING timesheets newer
                WHERE t."UserId" = newer."UserId"
                  AND t."WeekStartDate" = newer."WeekStartDate"
                  AND (t."GeneratedAt" < newer."GeneratedAt"
                       OR (t."GeneratedAt" = newer."GeneratedAt" AND t."Id" < newer."Id"));
                """);

            // Keep the most recently connected row for each (user, provider).
            migrationBuilder.Sql("""
                DELETE FROM connections c
                USING connections newer
                WHERE c."UserId" = newer."UserId"
                  AND c."Provider" = newer."Provider"
                  AND (c."ConnectedAt" < newer."ConnectedAt"
                       OR (c."ConnectedAt" = newer."ConnectedAt" AND c."Id" < newer."Id"));
                """);

            // Duplicate activities accumulated on every regenerate before this release.
            migrationBuilder.Sql("""
                DELETE FROM activities a
                USING activities newer
                WHERE a."UserId" = newer."UserId"
                  AND a."Source" = newer."Source"
                  AND a."Title" = newer."Title"
                  AND a."ActivityDate" = newer."ActivityDate"
                  AND a."Id" < newer."Id";
                """);

            // ---- Indexes -------------------------------------------------------------
            migrationBuilder.DropIndex(name: "IX_timesheets_UserId", table: "timesheets");
            migrationBuilder.DropIndex(name: "IX_connections_UserId", table: "connections");

            migrationBuilder.CreateIndex(
                name: "IX_timesheets_UserId_WeekStartDate",
                table: "timesheets",
                columns: new[] { "UserId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_timesheets_Status",
                table: "timesheets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_connections_UserId_Provider",
                table: "connections",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_activities_UserId_ActivityDate",
                table: "activities",
                columns: new[] { "UserId", "ActivityDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_activities_UserId_ActivityDate", table: "activities");
            migrationBuilder.DropIndex(name: "IX_connections_UserId_Provider", table: "connections");
            migrationBuilder.DropIndex(name: "IX_timesheets_Status", table: "timesheets");
            migrationBuilder.DropIndex(name: "IX_timesheets_UserId_WeekStartDate", table: "timesheets");

            migrationBuilder.CreateIndex(
                name: "IX_connections_UserId",
                table: "connections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_timesheets_UserId",
                table: "timesheets",
                column: "UserId");

            migrationBuilder.DropColumn(name: "LastSyncedAt", table: "connections");
            migrationBuilder.DropColumn(name: "LastError", table: "connections");
            migrationBuilder.DropColumn(name: "MissingHourPrompts", table: "timesheets");
        }
    }
}
