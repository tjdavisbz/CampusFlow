using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddChapelAttendanceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppChapelAttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TermCalendarId = table.Column<int>(type: "integer", nullable: false),
                    StudentUid = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChapelAtLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    LegacyAttendanceId = table.Column<int>(type: "integer", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppChapelAttendanceRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppChapelGroupMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TermCalendarId = table.Column<int>(type: "integer", nullable: false),
                    StudentUid = table.Column<int>(type: "integer", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppChapelGroupMemberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelAttendanceRecords_TenantId_LegacyAttendanceId",
                table: "AppChapelAttendanceRecords",
                columns: new[] { "TenantId", "LegacyAttendanceId" },
                unique: true,
                filter: "\"LegacyAttendanceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelAttendanceRecords_TenantId_TermCalendarId_ChapelAt~",
                table: "AppChapelAttendanceRecords",
                columns: new[] { "TenantId", "TermCalendarId", "ChapelAtLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelAttendanceRecords_TenantId_TermCalendarId_StudentU~",
                table: "AppChapelAttendanceRecords",
                columns: new[] { "TenantId", "TermCalendarId", "StudentUid", "ChapelAtLocal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelGroupMemberships_TenantId_TermCalendarId_GroupName",
                table: "AppChapelGroupMemberships",
                columns: new[] { "TenantId", "TermCalendarId", "GroupName" });

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelGroupMemberships_TenantId_TermCalendarId_StudentUi~",
                table: "AppChapelGroupMemberships",
                columns: new[] { "TenantId", "TermCalendarId", "StudentUid", "GroupName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppChapelAttendanceRecords");

            migrationBuilder.DropTable(
                name: "AppChapelGroupMemberships");
        }
    }
}
