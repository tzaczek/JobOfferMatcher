using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobOfferMatcher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_stage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "application",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application", x => x.offer_id);
                    table.ForeignKey(
                        name: "FK_application_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_pipeline_stage_current_stage_id",
                        column: x => x.current_stage_id,
                        principalTable: "pipeline_stage",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_communication",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_communication", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_communication_application_offer_id",
                        column: x => x.offer_id,
                        principalTable: "application",
                        principalColumn: "offer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_document", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_document_application_offer_id",
                        column: x => x.offer_id,
                        principalTable: "application",
                        principalColumn: "offer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_interview",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    interviewer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_interview", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_interview_application_offer_id",
                        column: x => x.offer_id,
                        principalTable: "application",
                        principalColumn: "offer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_note",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_note", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_note_application_offer_id",
                        column: x => x.offer_id,
                        principalTable: "application",
                        principalColumn: "offer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_task",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_task", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_task_application_offer_id",
                        column: x => x.offer_id,
                        principalTable: "application",
                        principalColumn: "offer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_current_stage_id",
                table: "application",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_status",
                table: "application",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_application_communication_offer_id",
                table: "application_communication",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_document_offer_id",
                table: "application_document",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_interview_offer_id",
                table: "application_interview",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_interview_scheduled_at",
                table: "application_interview",
                column: "scheduled_at");

            migrationBuilder.CreateIndex(
                name: "IX_application_note_offer_id",
                table: "application_note",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_task_offer_id",
                table: "application_task",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_task_offer_id_completed_at",
                table: "application_task",
                columns: new[] { "offer_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stage_position",
                table: "pipeline_stage",
                column: "position");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_communication");

            migrationBuilder.DropTable(
                name: "application_document");

            migrationBuilder.DropTable(
                name: "application_interview");

            migrationBuilder.DropTable(
                name: "application_note");

            migrationBuilder.DropTable(
                name: "application_task");

            migrationBuilder.DropTable(
                name: "application");

            migrationBuilder.DropTable(
                name: "pipeline_stage");
        }
    }
}
