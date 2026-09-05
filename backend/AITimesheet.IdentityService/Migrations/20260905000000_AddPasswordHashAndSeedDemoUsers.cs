using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AITimesheet.IdentityService.Migrations
{
    /// <summary>
    /// Adds credential storage and replaces the old "first login invents a manager"
    /// behaviour with an explicit demo reporting line.
    ///
    /// Existing rows get an empty PasswordHash, which can never match a PBKDF2 verify,
    /// so pre-existing accounts are locked out until a password is set for them. That is
    /// deliberate: no account should survive this migration without a credential.
    /// </summary>
    public partial class AddPasswordHashAndSeedDemoUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_UserId_Timestamp",
                table: "audit_logs",
                columns: new[] { "UserId", "Timestamp" });

            // Password for both demo accounts is "Demo@123" (see README). The salt is
            // fixed so this migration produces identical rows on every machine.
            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "AzureAdObjectId", "CreatedAt", "Email", "FullName", "IsActive", "ManagerId", "PasswordHash", "Role" },
                values: new object[]
                {
                    new Guid("2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58"),
                    null,
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    "sarah@company.com",
                    "Sarah Jenkins",
                    true,
                    null,
                    "v1.210000.OtcOUci5Tyag0T58W4gkbg==.gG5/8s0AXIhS3m+PHHR0o1v0gQwuIaNxnsGH4Io4Ggs=",
                    "Manager"
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "AzureAdObjectId", "CreatedAt", "Email", "FullName", "IsActive", "ManagerId", "PasswordHash", "Role" },
                values: new object[]
                {
                    new Guid("8f7d3c1e-1b64-4a2f-9d05-6c1a7e93b420"),
                    null,
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    "priya@company.com",
                    "Priya Sharma",
                    true,
                    new Guid("2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58"),
                    "v1.210000.nyxBq31eCMMWSpsC3ncxXw==.aWncJ51yUS7BXGfjejJdJguvbAIfuKPhJTH6zBC0GYw=",
                    "Employee"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "users",
                keyColumn: "Id",
                keyValue: new Guid("8f7d3c1e-1b64-4a2f-9d05-6c1a7e93b420"));

            migrationBuilder.DeleteData(
                table: "users",
                keyColumn: "Id",
                keyValue: new Guid("2c6b9a04-57e3-4f81-b3d7-0a94e2f16c58"));

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_UserId_Timestamp",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "users");
        }
    }
}
