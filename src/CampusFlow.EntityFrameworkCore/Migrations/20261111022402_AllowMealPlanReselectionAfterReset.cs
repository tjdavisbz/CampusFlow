using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AllowMealPlanReselectionAfterReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "StudentProfileId", "TermName" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "StudentProfileId", "TermName" },
                unique: true);
        }
    }
}
