using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoryAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_unseen_update",
                table: "offers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "offer_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_offer_event_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offer_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    fingerprint_algo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint_version = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_version", x => x.id);
                    table.ForeignKey(
                        name: "FK_offer_version_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_config",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    cron = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_config", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_event_offer_id",
                table: "offer_event",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_offer_version_offer_id",
                table: "offer_version",
                column: "offer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_event");

            migrationBuilder.DropTable(
                name: "offer_version");

            migrationBuilder.DropTable(
                name: "schedule_config");

            migrationBuilder.DropColumn(
                name: "has_unseen_update",
                table: "offers");
        }
    }
}
