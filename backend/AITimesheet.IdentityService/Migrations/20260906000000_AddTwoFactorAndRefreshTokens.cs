using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AITimesheet.IdentityService.Migrations
{
    /// <summary>
    /// Adds TOTP two-factor authentication and rotating refresh tokens.
    ///
    /// All three new user columns are nullable, so existing accounts are unaffected:
    /// two-factor is opt-in, and a user with no TotpEnabledAt keeps signing in with a
    /// password alone.
    /// </summary>
    public partial class AddTwoFactorAndRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Two-factor enrolment state on the user ------------------------------
            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TotpEnabledAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Replay guard: a TOTP code is valid for its whole 30-second window, so the
            // last accepted step is recorded to stop the same code being used twice.
            migrationBuilder.AddColumn<long>(
                name: "TotpLastUsedStep",
                table: "users",
                type: "bigint",
                nullable: true);

            // ---- Refresh tokens ------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    // Only the SHA-256 hash is stored; the token itself never lands here.
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    // All tokens descended from one login share a family, so reuse
                    // detection can revoke the entire chain at once.
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "text", nullable: true),
                    RevokedReason = table.Column<string>(type: "text", nullable: true),
                    CreatedByIp = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            // ---- Recovery codes ------------------------------------------------------
            migrationBuilder.CreateTable(
                name: "recovery_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_codes", x => x.Id);
                });

            // Every refresh is a lookup by hash — unique so a collision is impossible and
            // the read is a single index probe.
            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_FamilyId",
                table: "refresh_tokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_ExpiresAtUtc",
                table: "refresh_tokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_codes_UserId_UsedAtUtc",
                table: "recovery_codes",
                columns: new[] { "UserId", "UsedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "recovery_codes");
            migrationBuilder.DropTable(name: "refresh_tokens");

            migrationBuilder.DropColumn(name: "TotpLastUsedStep", table: "users");
            migrationBuilder.DropColumn(name: "TotpEnabledAt", table: "users");
            migrationBuilder.DropColumn(name: "TotpSecret", table: "users");
        }
    }
}
