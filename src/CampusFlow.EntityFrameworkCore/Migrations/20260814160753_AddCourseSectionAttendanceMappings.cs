using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseSectionAttendanceMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCourseSectionAttendanceTypeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SectionStart = table.Column<int>(type: "integer", nullable: false),
                    SectionEnd = table.Column<int>(type: "integer", nullable: false),
                    ExternalAttendanceTypeId = table.Column<int>(type: "integer", nullable: false),
                    AttendanceType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppCourseSectionAttendanceTypeMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSectionAttendanceTypeMappings_TenantId_IsActive_Ef~",
                table: "AppCourseSectionAttendanceTypeMappings",
                columns: new[] { "TenantId", "IsActive", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSectionAttendanceTypeMappings_TenantId_SectionStar~",
                table: "AppCourseSectionAttendanceTypeMappings",
                columns: new[] { "TenantId", "SectionStart", "SectionEnd", "AttendanceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCourseSectionAttendanceTypeMappings");
        }
    }
}
