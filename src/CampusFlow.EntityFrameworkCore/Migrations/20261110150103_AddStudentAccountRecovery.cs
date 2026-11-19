using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentAccountRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAccountRecoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TicketHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StudentUid = table.Column<int>(type: "integer", nullable: true),
                    LegacyStudentUid = table.Column<int>(type: "integer", nullable: true),
                    UserPrincipalName = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    DirectoryObjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<int>(type: "integer", nullable: true),
                    VerificationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PasswordWriteFinishedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AnswerAttempts = table.Column<int>(type: "integer", nullable: false),
                    PasswordAttempts = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAccountRecoveryRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAccountRecoveryVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentUid = table.Column<int>(type: "integer", nullable: false),
                    LegacyStudentUid = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: true),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AnswerHash = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    LegacyNoQuestionAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAccountRecoveryVerifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccountRecoveryRequests_TenantId_ClientHash_CreationTime",
                table: "AppAccountRecoveryRequests",
                columns: new[] { "TenantId", "ClientHash", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccountRecoveryRequests_TenantId_DirectoryObjectId_Creat~",
                table: "AppAccountRecoveryRequests",
                columns: new[] { "TenantId", "DirectoryObjectId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccountRecoveryRequests_TenantId_StudentUid_CreationTime",
                table: "AppAccountRecoveryRequests",
                columns: new[] { "TenantId", "StudentUid", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAccountRecoveryRequests_TenantId_TicketHash",
                table: "AppAccountRecoveryRequests",
                columns: new[] { "TenantId", "TicketHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppAccountRecoveryVerifications_TenantId_StudentUid",
                table: "AppAccountRecoveryVerifications",
                columns: new[] { "TenantId", "StudentUid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAccountRecoveryRequests");

            migrationBuilder.DropTable(
                name: "AppAccountRecoveryVerifications");
        }
    }
}
