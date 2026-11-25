using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audit.Worker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    course_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    registered_count = table.Column<int>(type: "integer", nullable: false),
                    remaining_seats = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.event_id);
                    table.CheckConstraint("ck_audit_logs_capacity", "capacity >= 0");
                    table.CheckConstraint("ck_audit_logs_registered_count", "registered_count >= 0");
                    table.CheckConstraint("ck_audit_logs_remaining_seats", "remaining_seats = capacity - registered_count");
                    table.CheckConstraint("ck_audit_logs_schema_version", "schema_version > 0");
                    table.CheckConstraint("ck_audit_logs_version", "version >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_course_section_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "course_section_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_event_type_occurred_at",
                table: "audit_logs",
                columns: new[] { "event_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_student_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "student_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
