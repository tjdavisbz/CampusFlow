using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardTermSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDashboardDefault",
                table: "AppRegistrationTermConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStudentSelectable",
                table: "AppRegistrationTermConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDashboardDefault",
                table: "AppRegistrationTermConfigurations");

            migrationBuilder.DropColumn(
                name: "IsStudentSelectable",
                table: "AppRegistrationTermConfigurations");
        }
    }
}
