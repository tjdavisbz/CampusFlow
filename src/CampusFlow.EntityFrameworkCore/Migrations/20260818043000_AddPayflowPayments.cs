using System;
using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20260818043000_AddPayflowPayments")]
public sealed class AddPayflowPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppPayflowPayments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                SecureTokenId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SecureToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                PayflowReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                GatewayResult = table.Column<int>(type: "integer", nullable: true),
                GatewayMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsTest = table.Column<bool>(type: "boolean", nullable: false),
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
                table.PrimaryKey("PK_AppPayflowPayments", x => x.Id);
                table.ForeignKey("FK_AppPayflowPayments_AppStudentProfiles_StudentProfileId", x => x.StudentProfileId,
                    "AppStudentProfiles", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_AppPayflowPayments_StudentProfileId", "AppPayflowPayments", "StudentProfileId");
        migrationBuilder.CreateIndex("IX_AppPayflowPayments_TenantId_SecureTokenId", "AppPayflowPayments",
            new[] { "TenantId", "SecureTokenId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("AppPayflowPayments");
}
