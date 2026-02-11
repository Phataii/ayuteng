using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ayuteng.Migrations
{
    /// <inheritdoc />
    public partial class sitevisitorupdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "SiteVisitors",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "SiteVisitors");
        }
    }
}
