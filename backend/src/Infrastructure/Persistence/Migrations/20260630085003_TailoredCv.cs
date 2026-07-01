using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TailoredCv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tailored_cv",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_cv_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    generation_version = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    emphasised_skills = table.Column<string>(type: "jsonb", nullable: false),
                    html_file_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pdf_file_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tailored_cv", x => x.offer_id);
                    table.ForeignKey(
                        name: "FK_tailored_cv_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tailored_cv_state",
                table: "tailored_cv",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tailored_cv");
        }
    }
}
