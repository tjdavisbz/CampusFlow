using System;
using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20260818230000_AddElementsPaymentPosting")]
public sealed class AddElementsPaymentPosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("ElementsPostingStatus", "AppPayflowPayments", "integer", nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<int>("ElementsPostingAttempts", "AppPayflowPayments", "integer", nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<int>("ElementsBatchMasterId", "AppPayflowPayments", "integer", nullable: true);
        migrationBuilder.AddColumn<int>("ElementsBillingBatchId", "AppPayflowPayments", "integer", nullable: true);
        migrationBuilder.AddColumn<string>("ElementsPostingError", "AppPayflowPayments",
            "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ElementsPostedAt", "AppPayflowPayments",
            "timestamp without time zone", nullable: true);

        migrationBuilder.Sql("""
            UPDATE "AppPayflowPayments"
            SET "ElementsPostingStatus" = CASE WHEN "Status" = 2 THEN 1 ELSE 0 END;
            """);
        migrationBuilder.CreateIndex("IX_AppPayflowPayments_ElementsPostingStatus", "AppPayflowPayments",
            "ElementsPostingStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_AppPayflowPayments_ElementsPostingStatus", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsPostingStatus", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsPostingAttempts", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsBatchMasterId", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsBillingBatchId", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsPostingError", "AppPayflowPayments");
        migrationBuilder.DropColumn("ElementsPostedAt", "AppPayflowPayments");
    }
}
