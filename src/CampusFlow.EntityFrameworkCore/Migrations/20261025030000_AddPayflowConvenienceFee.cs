using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20261025030000_AddPayflowConvenienceFee")]
public sealed class AddPayflowConvenienceFee : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ConvenienceFee",
            table: "AppPayflowPayments",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "ElementsFeeBillingBatchId",
            table: "AppPayflowPayments",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ConvenienceFee", table: "AppPayflowPayments");
        migrationBuilder.DropColumn(name: "ElementsFeeBillingBatchId", table: "AppPayflowPayments");
    }
}
