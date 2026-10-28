using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddChapelFeedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppChapelFeedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ScanCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppChapelFeedFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppChapelFeedFiles_TenantId_Sha256",
                table: "AppChapelFeedFiles",
                columns: new[] { "TenantId", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppChapelFeedFiles");
        }
    }
}
