using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Offers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    native_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    identity_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    company = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    salary_bands = table.Column<string>(type: "jsonb", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    work_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    seniority = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    required_skills = table.Column<string>(type: "jsonb", nullable: false),
                    nice_skills = table.Column<string>(type: "jsonb", nullable: false),
                    description_html = table.Column<string>(type: "text", nullable: true),
                    canonical_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fingerprint_algo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint_version = table.Column<int>(type: "integer", nullable: false),
                    current_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_suggested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    availability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    disappeared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    role_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scan_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    window_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_ids = table.Column<string>(type: "jsonb", nullable: false),
                    count_collected = table.Column<int>(type: "integer", nullable: false),
                    count_new = table.Column<int>(type: "integer", nullable: false),
                    count_updated = table.Column<int>(type: "integer", nullable: false),
                    count_unavailable = table.Column<int>(type: "integer", nullable: false),
                    count_failed = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    incomplete_reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offer_observation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fingerprint_algo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint_version = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_observation", x => x.id);
                    table.ForeignKey(
                        name: "FK_offer_observation_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_observation_offer_id",
                table: "offer_observation",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_offer_observation_scan_run_id",
                table: "offer_observation",
                column: "scan_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_offers_last_seen_at",
                table: "offers",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_offers_source_id_native_key",
                table: "offers",
                columns: new[] { "source_id", "native_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offers_user_status",
                table: "offers",
                column: "user_status");

            migrationBuilder.CreateIndex(
                name: "IX_scan_run_window_utc_trigger",
                table: "scan_run",
                columns: new[] { "window_utc", "trigger" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_observation");

            migrationBuilder.DropTable(
                name: "scan_run");

            migrationBuilder.DropTable(
                name: "offers");
        }
    }
}
