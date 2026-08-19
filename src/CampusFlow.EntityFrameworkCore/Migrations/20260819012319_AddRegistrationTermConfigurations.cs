using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationTermConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppRegistrationTermConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TermName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RegistrationOpensAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RegistrationClosesAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequireAdvisorReview = table.Column<bool>(type: "boolean", nullable: false),
                    EnforceSectionCapacity = table.Column<bool>(type: "boolean", nullable: false),
                    AttendanceTypeMappingsJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AppRegistrationTermConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppRegistrationTermConfigurations_TenantId_ExternalTermId",
                table: "AppRegistrationTermConfigurations",
                columns: new[] { "TenantId", "ExternalTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRegistrationTermConfigurations_TenantId_IsEnabled_Regist~",
                table: "AppRegistrationTermConfigurations",
                columns: new[] { "TenantId", "IsEnabled", "RegistrationOpensAt", "RegistrationClosesAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppRegistrationTermConfigurations");
        }
    }
}
