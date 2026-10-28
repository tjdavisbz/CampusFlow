using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddChapelExcuseApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AppChapelExcuseRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "AppChapelExcuseRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceRecordsExcused",
                table: "AppChapelExcuseRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "AppChapelExcuseRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelExcuseRequests_TenantId_TermCalendarId_Status_Crea~",
                table: "AppChapelExcuseRequests",
                columns: new[] { "TenantId", "TermCalendarId", "Status", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppChapelExcuseRequests_TenantId_TermCalendarId_Status_Crea~",
                table: "AppChapelExcuseRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "AppChapelExcuseRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "AppChapelExcuseRequests");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordsExcused",
                table: "AppChapelExcuseRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AppChapelExcuseRequests");
        }
    }
}
