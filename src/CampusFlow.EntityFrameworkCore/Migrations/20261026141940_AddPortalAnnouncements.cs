using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPortalAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    TermsJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppPortalAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppPortalAnnouncements_TenantId_IsPublished_StartsAtUtc_End~",
                table: "AppPortalAnnouncements",
                columns: new[] { "TenantId", "IsPublished", "StartsAtUtc", "EndsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPortalAnnouncements");
        }
    }
}
