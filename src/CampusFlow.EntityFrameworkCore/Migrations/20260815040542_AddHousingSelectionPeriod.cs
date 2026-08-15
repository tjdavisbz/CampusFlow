using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingSelectionPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId",
                table: "AppStudentHousingSelections");

            migrationBuilder.AddColumn<DateTime>(
                name: "TermEndDate",
                table: "AppStudentHousingSelections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermName",
                table: "AppStudentHousingSelections",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TermStartDate",
                table: "AppStudentHousingSelections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AppStudentHousingSelections"
                SET "TermName" = to_char("SubmittedAt", 'FMMonth YYYY')
                WHERE "TermName" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "StudentProfileId", "TermName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId_TermN~",
                table: "AppStudentHousingSelections");

            migrationBuilder.DropColumn(
                name: "TermEndDate",
                table: "AppStudentHousingSelections");

            migrationBuilder.DropColumn(
                name: "TermName",
                table: "AppStudentHousingSelections");

            migrationBuilder.DropColumn(
                name: "TermStartDate",
                table: "AppStudentHousingSelections");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "StudentProfileId" },
                unique: true);
        }
    }
}
