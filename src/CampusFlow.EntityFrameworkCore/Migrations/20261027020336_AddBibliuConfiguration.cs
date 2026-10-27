using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddBibliuConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppBibliuConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BibliuTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoreNumber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    InSiteStoreNumber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppBibliuConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppBibliuConfigurations_TenantId",
                table: "AppBibliuConfigurations",
                column: "TenantId",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppBibliuConfigurations");
        }
    }
}
