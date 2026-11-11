using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AllowBillReapprovalAfterReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppBillApprovals_TenantId_UserId_ExternalTermId",
                table: "AppBillApprovals");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovals_TenantId_UserId_ExternalTermId",
                table: "AppBillApprovals",
                columns: new[] { "TenantId", "UserId", "ExternalTermId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppBillApprovals_TenantId_UserId_ExternalTermId",
                table: "AppBillApprovals");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovals_TenantId_UserId_ExternalTermId",
                table: "AppBillApprovals",
                columns: new[] { "TenantId", "UserId", "ExternalTermId" },
                unique: true);
        }
    }
}
