using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddBillApprovalTermConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppBillApprovalTermConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TermName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OpensAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClosesAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AgreementTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentPlanPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AppBillApprovalTermConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppBillApprovalTermConfigurations_AppAgreementTemplates_Agr~",
                        column: x => x.AgreementTemplateId,
                        principalTable: "AppAgreementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppBillApprovalTermConfigurations_AppPaymentPlanPolicies_Pa~",
                        column: x => x.PaymentPlanPolicyId,
                        principalTable: "AppPaymentPlanPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovalTermConfigurations_AgreementTemplateId",
                table: "AppBillApprovalTermConfigurations",
                column: "AgreementTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovalTermConfigurations_PaymentPlanPolicyId",
                table: "AppBillApprovalTermConfigurations",
                column: "PaymentPlanPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovalTermConfigurations_TenantId_ExternalTermId",
                table: "AppBillApprovalTermConfigurations",
                columns: new[] { "TenantId", "ExternalTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovalTermConfigurations_TenantId_IsEnabled_OpensA~",
                table: "AppBillApprovalTermConfigurations",
                columns: new[] { "TenantId", "IsEnabled", "OpensAt", "ClosesAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppBillApprovalTermConfigurations");
        }
    }
}
