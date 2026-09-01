using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20260814042735_AddBillApprovalPdfSnapshot")]
public partial class AddBillApprovalPdfSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReviewSnapshotJson",
            table: "AppBillApprovals",
            type: "text",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.AddColumn<string>(
            name: "PdfBlobName",
            table: "AppBillApprovalArtifacts",
            type: "character varying(512)",
            maxLength: 512,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReviewSnapshotJson", table: "AppBillApprovals");
        migrationBuilder.DropColumn(name: "PdfBlobName", table: "AppBillApprovalArtifacts");
    }
}
