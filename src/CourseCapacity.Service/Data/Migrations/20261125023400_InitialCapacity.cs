using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourseCapacity.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outbox_messages", x => x.event_id);
                    table.CheckConstraint("ck_outbox_version_nonnegative", "version >= 0");
                });

            migrationBuilder.CreateTable(
                name: "processed_operations",
                columns: table => new
                {
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    outcome_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_processed_operations", x => new { x.actor_id, x.operation_id });
                });

            migrationBuilder.CreateTable(
                name: "semesters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_semesters", x => x.id);
                    table.CheckConstraint("ck_semesters_date_range", "start_date <= end_date");
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    credits = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_subjects", x => x.id);
                    table.CheckConstraint("ck_subjects_credits_positive", "credits > 0");
                });

            migrationBuilder.CreateTable(
                name: "course_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lecturer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    registered_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_course_sections", x => x.id);
                    table.CheckConstraint("ck_course_sections_capacity_positive", "capacity > 0");
                    table.CheckConstraint("ck_course_sections_registered_count", "registered_count >= 0 AND registered_count <= capacity");
                    table.CheckConstraint("ck_course_sections_status", "status IN ('Open', 'Closed')");
                    table.CheckConstraint("ck_course_sections_version_nonnegative", "version >= 0");
                    table.ForeignKey(
                        name: "f_k_course_sections_semesters_semester_id",
                        column: x => x.semester_id,
                        principalTable: "semesters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_course_sections_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "course_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    room = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_course_schedules", x => x.id);
                    table.CheckConstraint("ck_course_schedules_time_range", "start_time < end_time");
                    table.CheckConstraint("ck_course_schedules_weekday", "day_of_week BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "f_k_course_schedules_course_sections_course_section_id",
                        column: x => x.course_section_id,
                        principalTable: "course_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_registrations", x => x.id);
                    table.CheckConstraint("ck_registrations_cancelled_at", "(status = 'Active' AND cancelled_at IS NULL) OR (status = 'Cancelled' AND cancelled_at IS NOT NULL)");
                    table.CheckConstraint("ck_registrations_status", "status IN ('Active', 'Cancelled')");
                    table.ForeignKey(
                        name: "f_k_registrations_course_sections_course_section_id",
                        column: x => x.course_section_id,
                        principalTable: "course_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_course_schedules_slot",
                table: "course_schedules",
                columns: new[] { "course_section_id", "day_of_week", "start_time", "end_time", "room" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_course_sections_subject_id",
                table: "course_sections",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_course_sections_semester_course_code",
                table: "course_sections",
                columns: new[] { "semester_id", "course_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_outbox_messages_published_at_occurred_at_event_id",
                table: "outbox_messages",
                columns: new[] { "published_at", "occurred_at", "event_id" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_registrations_course_section_id_status",
                table: "registrations",
                columns: new[] { "course_section_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_registrations_student_id_status",
                table: "registrations",
                columns: new[] { "student_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_registrations_active_student_section",
                table: "registrations",
                columns: new[] { "student_id", "course_section_id" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_semesters_code",
                table: "semesters",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subjects_code",
                table: "subjects",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_schedules");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "processed_operations");

            migrationBuilder.DropTable(
                name: "registrations");

            migrationBuilder.DropTable(
                name: "course_sections");

            migrationBuilder.DropTable(
                name: "semesters");

            migrationBuilder.DropTable(
                name: "subjects");
        }
    }
}
