using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    industry = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    timezone = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seed_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    source_file = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    account_rows = table.Column<int>(type: "int", nullable: false),
                    event_rows = table.Column<int>(type: "int", nullable: false),
                    applied_at_utc = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seed_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    account_id = table.Column<int>(type: "int", nullable: false),
                    location = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    duration_seconds = table.Column<int>(type: "int", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    occurred_local_date = table.Column<DateOnly>(type: "date", nullable: true),
                    local_week_start = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_activity_events_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_account_local_week",
                table: "activity_events",
                columns: new[] { "account_id", "local_week_start" })
                .Annotation("SqlServer:Include", new[] { "location", "event_type" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_dedup",
                table: "activity_events",
                columns: new[] { "account_id", "location", "event_type", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropTable(
                name: "seed_runs");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
