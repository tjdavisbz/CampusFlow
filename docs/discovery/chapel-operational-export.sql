/*
  Read-only Chapel follow-up export for the Nelson Fall 2026 migration.
  Run in SSMS against the SQL Server containing Nelson_ReportingAndIntegration.
  No backups, temporary tables, or application-data changes are made.

  The attendance history is already covered by chapel-fall-2026-history-export.sql.
  Save each result set separately, with column headers, and transfer it securely:
  the results contain student identifiers and comments. Do not commit exports.
*/

USE [Nelson_ReportingAndIntegration];
GO
SET NOCOUNT ON;

DECLARE @TermCalendarID int = 268; -- Fall 2026

SELECT N'ChapelSchedule' AS SourceTable, ChapelScheduleID, TermCalendarID,
       [Date], TimeFrom, TimeTo, DormDevo, DayOfWeek
FROM dbo.ChapelSchedule
WHERE TermCalendarID = @TermCalendarID
ORDER BY [Date], ChapelScheduleID;

SELECT N'ChapelExcuses' AS SourceTable, ChapelExcusesID, TermCalendarID,
       StudentUID, Comment, Lastname, Firstname, Middle,
       Mon, Tue, Wed, Thu, Fri
FROM dbo.ChapelExcuses
WHERE TermCalendarID = @TermCalendarID
ORDER BY StudentUID, ChapelExcusesID;

SELECT N'ChapelBonusBank' AS SourceTable, ChapelBonusBankID, TermCalendarID,
       StudentUID, Lastname, Firstname, Middle, [Count]
FROM dbo.ChapelBonusBank
WHERE TermCalendarID = @TermCalendarID
ORDER BY StudentUID, ChapelBonusBankID;

-- The legacy ChapelGroups table has no term column. Export it in full so that
-- group memberships are not incorrectly inferred from current attendance.
SELECT N'ChapelGroups' AS SourceTable, *
FROM dbo.ChapelGroups
ORDER BY StudentUID, ChapelGroupsID;

-- The exclusion list also has no term column and can contain students who
-- were intentionally omitted from the Fall attendance population.
SELECT N'Chapel_ExclusionList' AS SourceTable, StudentUID
FROM dbo.Chapel_ExclusionList
ORDER BY StudentUID;

SELECT N'ChapelReleaseRequests' AS SourceTable, StudentUID, TermCalendarID,
       BeginDate, ExcuseType, Reason, Mon, Tue, Wed, Thu, Fri,
       EmployerName, EmployerPhone, EmployerEmail
FROM dbo.ChapelReleaseRequests
WHERE TermCalendarID = @TermCalendarID
ORDER BY StudentUID, BeginDate;

-- The compiled legacy staff portal writes SAGU_ChapelBonusBank, whereas the
-- supplied table definition is ChapelBonusBank in another database. This
-- read-only metadata check identifies which objects exist here; do not assume
-- these are the same table until their rows are compared.
SELECT DB_NAME() AS DatabaseName, s.name AS SchemaName, o.name AS ObjectName,
       o.type_desc AS ObjectType
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.name IN (N'ChapelBonusBank', N'SAGU_ChapelBonusBank')
ORDER BY s.name, o.name;

IF DB_ID(N'CAMs_Enterprise') IS NOT NULL
BEGIN
    EXEC (N'SELECT N''CAMs_Enterprise'' AS DatabaseName, s.name AS SchemaName,
                   o.name AS ObjectName, o.type_desc AS ObjectType
            FROM CAMs_Enterprise.sys.objects o
            JOIN CAMs_Enterprise.sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.name IN (N''ChapelBonusBank'', N''SAGU_ChapelBonusBank'')
            ORDER BY s.name, o.name;');

    IF OBJECT_ID(N'CAMs_Enterprise.dbo.SAGU_ChapelBonusBank', N'U') IS NOT NULL
        EXEC (N'SELECT N''CAMs_Enterprise.dbo.SAGU_ChapelBonusBank'' AS SourceTable, *
                FROM CAMs_Enterprise.dbo.SAGU_ChapelBonusBank
                WHERE TermCalendarID = 268
                ORDER BY StudentUID;');
END;
