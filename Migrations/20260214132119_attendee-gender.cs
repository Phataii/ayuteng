using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ayuteng.Migrations
{
    /// <inheritdoc />
    public partial class attendeegender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RegistrationNotes",
                table: "MeetingAttendees",
                newName: "Gender");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Gender",
                table: "MeetingAttendees",
                newName: "RegistrationNotes");
        }
    }
}
