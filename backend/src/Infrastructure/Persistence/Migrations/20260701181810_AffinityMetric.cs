using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AffinityMetric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offer_affinity",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: true),
                    resembles = table.Column<string>(type: "jsonb", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: true),
                    inputs_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    produced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_affinity", x => x.offer_id);
                    table.ForeignKey(
                        name: "FK_offer_affinity_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offer_affinity_state",
                table: "offer_affinity",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_affinity");
        }
    }
}
