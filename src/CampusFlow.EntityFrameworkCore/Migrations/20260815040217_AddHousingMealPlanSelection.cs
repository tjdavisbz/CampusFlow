using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingMealPlanSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppMealPlanConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalMealPlanId = table.Column<int>(type: "integer", nullable: true),
                    ExternalMealPlanName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HousingChoicesJson = table.Column<string>(type: "text", nullable: false),
                    EligibleAttendanceTypesJson = table.Column<string>(type: "text", nullable: false),
                    DisplayPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsNoPlanOption = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppMealPlanConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppStudentHousingSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HousingChoice = table.Column<int>(type: "integer", nullable: false),
                    ExternalMealPlanId = table.Column<int>(type: "integer", nullable: true),
                    MealPlanName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SyncedToStudentInformationSystem = table.Column<bool>(type: "boolean", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_AppStudentHousingSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppStudentHousingSelections_AppStudentProfiles_StudentProfi~",
                        column: x => x.StudentProfileId,
                        principalTable: "AppStudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMealPlanConfigurations_TenantId_ExternalMealPlanName",
                table: "AppMealPlanConfigurations",
                columns: new[] { "TenantId", "ExternalMealPlanName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMealPlanConfigurations_TenantId_IsActive_SortOrder",
                table: "AppMealPlanConfigurations",
                columns: new[] { "TenantId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_StudentProfileId",
                table: "AppStudentHousingSelections",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_StudentProfileId",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "StudentProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStudentHousingSelections_TenantId_SyncedToStudentInforma~",
                table: "AppStudentHousingSelections",
                columns: new[] { "TenantId", "SyncedToStudentInformationSystem", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMealPlanConfigurations");

            migrationBuilder.DropTable(
                name: "AppStudentHousingSelections");
        }
    }
}
