using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LlmEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the recomputable keyword cache (no user data — Principle IX) and add the AI profile
            // as a fresh nullable jsonb column. NOT a rename: the old CandidateProfile JSON is a
            // different shape and must not be reinterpreted as the new CvProfile (ADR-2).
            migrationBuilder.DropColumn(
                name: "derived_profile",
                table: "candidate_cv");

            migrationBuilder.AddColumn<string>(
                name: "profile",
                table: "candidate_cv",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "enrichment_input_hash",
                table: "candidate_cv",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "profile_attempts",
                table: "candidate_cv",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "profile_produced_at",
                table: "candidate_cv",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "profile_state",
                table: "candidate_cv",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            // Seed the existing app_settings singleton with the default enrichment config (camelCase
            // to match the jsonb web-options converter; FR-018 defaults).
            migrationBuilder.AddColumn<string>(
                name: "enrichment",
                table: "app_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"offerSummaryMaxWords\":60,\"cvSummaryMaxWords\":60,\"maxKeySkills\":10,\"fitRationaleMaxWords\":30,\"retryLimit\":3}");

            migrationBuilder.CreateTable(
                name: "offer_enrichment",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    key_skills = table.Column<string>(type: "jsonb", nullable: false),
                    inputs_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    produced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_enrichment", x => x.offer_id);
                    table.ForeignKey(
                        name: "FK_offer_enrichment_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offer_fit",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: true),
                    matched = table.Column<string>(type: "jsonb", nullable: false),
                    missing = table.Column<string>(type: "jsonb", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: true),
                    inputs_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    produced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_fit", x => x.offer_id);
                    table.ForeignKey(
                        name: "FK_offer_fit_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_enrichment_state",
                table: "offer_enrichment",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_offer_fit_state",
                table: "offer_fit",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_enrichment");

            migrationBuilder.DropTable(
                name: "offer_fit");

            migrationBuilder.DropColumn(
                name: "enrichment_input_hash",
                table: "candidate_cv");

            migrationBuilder.DropColumn(
                name: "profile_attempts",
                table: "candidate_cv");

            migrationBuilder.DropColumn(
                name: "profile_produced_at",
                table: "candidate_cv");

            migrationBuilder.DropColumn(
                name: "profile_state",
                table: "candidate_cv");

            migrationBuilder.DropColumn(
                name: "enrichment",
                table: "app_settings");

            migrationBuilder.DropColumn(
                name: "profile",
                table: "candidate_cv");

            migrationBuilder.AddColumn<string>(
                name: "derived_profile",
                table: "candidate_cv",
                type: "jsonb",
                nullable: true);
        }
    }
}
