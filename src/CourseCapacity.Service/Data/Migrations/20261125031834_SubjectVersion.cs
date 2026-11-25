using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourseCapacity.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubjectVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "subjects",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "subjects");
        }
    }
}
