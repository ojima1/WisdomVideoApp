using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisdomVideoApp.Migrations
{
    /// <inheritdoc />
    public partial class inddyd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StyleId",
                table: "VideoJobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StyleId",
                table: "VideoJobs");
        }
    }
}
