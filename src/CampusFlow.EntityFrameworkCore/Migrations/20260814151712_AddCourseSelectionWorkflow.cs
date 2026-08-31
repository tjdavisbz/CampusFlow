using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseSelectionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAdvisorAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttendanceType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExternalAdvisorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdvisorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AdvisorDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppAdvisorAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCourseReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalCourseOfferingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalCourseRegistrationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttendanceType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CourseSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ExternalAdvisorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AdvisorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    AdvisorComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RemovalStatus = table.Column<int>(type: "integer", nullable: false),
                    RemovalAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastRemovalAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastRemovalError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_AppCourseReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCourseReviews_AppStudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "AppStudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppCourseReviewSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvisorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OverallComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DecisionsSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EmailStatus = table.Column<int>(type: "integer", nullable: false),
                    EmailAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastEmailAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastEmailError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_AppCourseReviewSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCourseReviewSubmissions_AppStudentProfiles_StudentProfil~",
                        column: x => x.StudentProfileId,
                        principalTable: "AppStudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppCourseSelectionOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalStudentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalTermId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalCourseOfferingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CourseSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExternalCourseRegistrationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CourseReviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_AppCourseSelectionOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCourseSelectionOperations_AppStudentProfiles_StudentProf~",
                        column: x => x.StudentProfileId,
                        principalTable: "AppStudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppCourseSelectionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    RequireAdvisorReview = table.Column<bool>(type: "boolean", nullable: false),
                    EnforceSectionCapacity = table.Column<bool>(type: "boolean", nullable: false),
                    AttendanceTypeMappingsJson = table.Column<string>(type: "text", nullable: false),
                    EligibleTermRulesJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AppCourseSelectionPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAdvisorAssignments_TenantId_AdvisorEmail_IsActive",
                table: "AppAdvisorAssignments",
                columns: new[] { "TenantId", "AdvisorEmail", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAdvisorAssignments_TenantId_AttendanceType_EffectiveFrom",
                table: "AppAdvisorAssignments",
                columns: new[] { "TenantId", "AttendanceType", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviews_StudentProfileId",
                table: "AppCourseReviews",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviews_TenantId_ExternalAdvisorId_ExternalTermId_~",
                table: "AppCourseReviews",
                columns: new[] { "TenantId", "ExternalAdvisorId", "ExternalTermId", "NeedsReview" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviews_TenantId_ExternalCourseRegistrationId",
                table: "AppCourseReviews",
                columns: new[] { "TenantId", "ExternalCourseRegistrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviews_TenantId_StudentProfileId_ExternalTermId_N~",
                table: "AppCourseReviews",
                columns: new[] { "TenantId", "StudentProfileId", "ExternalTermId", "NeedsReview" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviewSubmissions_StudentProfileId",
                table: "AppCourseReviewSubmissions",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseReviewSubmissions_TenantId_StudentProfileId_Extern~",
                table: "AppCourseReviewSubmissions",
                columns: new[] { "TenantId", "StudentProfileId", "ExternalTermId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSelectionOperations_StudentProfileId",
                table: "AppCourseSelectionOperations",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSelectionOperations_TenantId_IdempotencyKey",
                table: "AppCourseSelectionOperations",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSelectionOperations_TenantId_Status_LastAttemptAt",
                table: "AppCourseSelectionOperations",
                columns: new[] { "TenantId", "Status", "LastAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSelectionPolicies_TenantId_IsPublished_EffectiveFr~",
                table: "AppCourseSelectionPolicies",
                columns: new[] { "TenantId", "IsPublished", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCourseSelectionPolicies_TenantId_Name_Version",
                table: "AppCourseSelectionPolicies",
                columns: new[] { "TenantId", "Name", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAdvisorAssignments");

            migrationBuilder.DropTable(
                name: "AppCourseReviews");

            migrationBuilder.DropTable(
                name: "AppCourseReviewSubmissions");

            migrationBuilder.DropTable(
                name: "AppCourseSelectionOperations");

            migrationBuilder.DropTable(
                name: "AppCourseSelectionPolicies");
        }
    }
}
