using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyBillApprovalHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppLegacyBillApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TermName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceApproved = table.Column<bool>(type: "boolean", nullable: false),
                    LoginTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AcceptTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SourceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRowNumber = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_AppLegacyBillApprovals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLegacyBillApprovals_TenantId_ExternalStudentId_ExternalT~",
                table: "AppLegacyBillApprovals",
                columns: new[] { "TenantId", "ExternalStudentId", "ExternalTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLegacyBillApprovals_TenantId_ExternalTermId_SourceApprov~",
                table: "AppLegacyBillApprovals",
                columns: new[] { "TenantId", "ExternalTermId", "SourceApproved" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLegacyBillApprovals");
        }
    }
}
