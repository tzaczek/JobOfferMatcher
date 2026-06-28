using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_source",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    search_criteria = table.Column<string>(type: "jsonb", nullable: false),
                    requires_login = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_source", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_source_name",
                table: "job_source",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_source");
        }
    }
}
