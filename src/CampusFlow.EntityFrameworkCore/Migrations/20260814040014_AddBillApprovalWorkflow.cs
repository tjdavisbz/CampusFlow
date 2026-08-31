using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddBillApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAgreementTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    AllowedMergeFieldsJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AppAgreementTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPaymentPlanPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EnrollmentFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PartTimeBalanceDivisor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResidentialMinimumPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StandardMinimumPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ResidentialAttendanceTypesJson = table.Column<string>(type: "text", nullable: false),
                    FallDueDatesJson = table.Column<string>(type: "text", nullable: false),
                    SpringDueDatesJson = table.Column<string>(type: "text", nullable: false),
                    SummerDueDatesJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AppPaymentPlanPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppBillApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TermName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PaymentChoice = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChargesTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditsTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnticipatedAidTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentPlanFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentScheduleJson = table.Column<string>(type: "text", nullable: false),
                    AgreementTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgreementTemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    RenderedAgreementSnapshot = table.Column<string>(type: "text", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AppBillApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppBillApprovals_AppAgreementTemplates_AgreementTemplateId",
                        column: x => x.AgreementTemplateId,
                        principalTable: "AppAgreementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppBillApprovals_AppStudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "AppStudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppBillApprovalArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PdfFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PdfSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ElementsDocumentTrackingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PdfStatus = table.Column<int>(type: "integer", nullable: false),
                    DocumentUploadStatus = table.Column<int>(type: "integer", nullable: false),
                    StudentEmailStatus = table.Column<int>(type: "integer", nullable: false),
                    BillingEmailStatus = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_AppBillApprovalArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppBillApprovalArtifacts_AppBillApprovals_BillApprovalId",
                        column: x => x.BillApprovalId,
                        principalTable: "AppBillApprovals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAgreementTemplates_TenantId_IsPublished_EffectiveFrom",
                table: "AppAgreementTemplates",
                columns: new[] { "TenantId", "IsPublished", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAgreementTemplates_TenantId_Name_Version",
                table: "AppAgreementTemplates",
                columns: new[] { "TenantId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovalArtifacts_BillApprovalId",
                table: "AppBillApprovalArtifacts",
                column: "BillApprovalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovals_AgreementTemplateId",
                table: "AppBillApprovals",
                column: "AgreementTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovals_StudentProfileId",
                table: "AppBillApprovals",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppBillApprovals_TenantId_UserId_ExternalTermId",
                table: "AppBillApprovals",
                columns: new[] { "TenantId", "UserId", "ExternalTermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPaymentPlanPolicies_TenantId_IsPublished_EffectiveFrom",
                table: "AppPaymentPlanPolicies",
                columns: new[] { "TenantId", "IsPublished", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPaymentPlanPolicies_TenantId_Name_Version",
                table: "AppPaymentPlanPolicies",
                columns: new[] { "TenantId", "Name", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppBillApprovalArtifacts");

            migrationBuilder.DropTable(
                name: "AppPaymentPlanPolicies");

            migrationBuilder.DropTable(
                name: "AppBillApprovals");

            migrationBuilder.DropTable(
                name: "AppAgreementTemplates");
        }
    }
}
