using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFlow.Migrations;

[DbContext(typeof(CampusFlowDbContext))]
[Migration("20261102163000_AddCourseSelectionGroupWindows")]
public sealed class AddCourseSelectionGroupWindows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllowAllStudentsDuringWindow",
            table: "AppRegistrationTermConfigurations",
            type: "boolean",
            nullable: false,
            defaultValue: true);
        migrationBuilder.AddColumn<string>(
            name: "GroupWindowsJson",
            table: "AppRegistrationTermConfigurations",
            type: "text",
            nullable: false,
            defaultValue: "[]");

        // Preserve existing global advisor access before the old permission is retired.
        // Only already-linked, active staff users can be migrated from ABP identity here.
        migrationBuilder.Sql("""
            WITH legacy_usernames AS (
                SELECT unnest(ARRAY[
                    'tjdavis','jlroberts','bbaker','hsellers','cslusher','cdarby','iurrutia',
                    'mzamorano','ezamorano','cbaptiste','jruiz','mwalker','kodell','bnewell',
                    'nfunchess','cmaynard','arossie','zdagostino','cmaynard2','ishelley',
                    'aodell','gdorsett','aferguson','horrill','tpolk','nevans','pjones',
                    'berwin','jmanguno','jmarquez','kbock','agregurich','mskelton',
                    'alemley','kwarnock','sstoffel'
                ]) AS username
            ), global_users AS (
                SELECT DISTINCT ON (u."TenantId", lower(u."Email"))
                    u."TenantId", u."Email", u."UserName", u."Name", u."Surname"
                FROM "AbpUsers" u
                WHERE u."IsActive" = true AND u."IsDeleted" = false
                  AND nullif(trim(u."Email"), '') IS NOT NULL
                  AND lower(u."Email") NOT LIKE '%@my.nelson.edu'
                  AND (
                    lower(u."UserName") IN (SELECT username FROM legacy_usernames)
                    OR EXISTS (
                        SELECT 1 FROM "AbpPermissionGrants" p
                        WHERE p."Name" = 'CampusFlow.AdvisorPortal.ViewAll'
                          AND p."TenantId" IS NOT DISTINCT FROM u."TenantId"
                          AND p."ProviderName" = 'U' AND p."ProviderKey" = u."Id"::text
                    )
                    OR EXISTS (
                        SELECT 1 FROM "AbpUserRoles" ur
                        JOIN "AbpRoles" r ON r."Id" = ur."RoleId"
                        LEFT JOIN "AbpPermissionGrants" p ON p."ProviderName" = 'R'
                          AND lower(p."ProviderKey") = lower(r."Name")
                          AND p."Name" = 'CampusFlow.AdvisorPortal.ViewAll'
                          AND p."TenantId" IS NOT DISTINCT FROM u."TenantId"
                        WHERE ur."UserId" = u."Id"
                          AND (p."Id" IS NOT NULL OR lower(r."Name") = 'advisor-global-reviewer')
                    )
                  )
                ORDER BY u."TenantId", lower(u."Email"), u."Id"
            )
            INSERT INTO "AppAdvisorAssignments" (
                "Id", "TenantId", "AttendanceType", "ExternalAdvisorId",
                "AdvisorEmail", "AdvisorDisplayName", "EffectiveFrom", "IsActive",
                "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
            SELECT gen_random_uuid(), gu."TenantId", 'Global', '', gu."Email",
                   coalesce(nullif(trim(concat_ws(' ', gu."Name", gu."Surname")), ''), gu."UserName"),
                   timezone('utc', now()), true, '{}', gen_random_uuid()::text,
                   timezone('utc', now()), false
            FROM global_users gu
            WHERE NOT EXISTS (
                SELECT 1 FROM "AppAdvisorAssignments" a
                WHERE a."TenantId" IS NOT DISTINCT FROM gu."TenantId"
                  AND lower(a."AdvisorEmail") = lower(gu."Email")
                  AND a."AttendanceType" = 'Global' AND a."IsActive" = true
                  AND a."IsDeleted" = false
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GroupWindowsJson", table: "AppRegistrationTermConfigurations");
        migrationBuilder.DropColumn(name: "AllowAllStudentsDuringWindow", table: "AppRegistrationTermConfigurations");
    }
}
