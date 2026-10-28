/*
  Read-only Chapel follow-up export for Nelson Fall 2026 (TermCalendarID 268).
  Run in SSMS on the SQL Server that contains CAMs_Enterprise. The operational
  Chapel schedule, excuses, groups, and exclusion data are in CAMs_Enterprise.
  The active bonus and request tables use SAGU_ names in CAMs_Enterprise.
  Similar unprefixed tables also exist in Nelson_ReportingAndIntegration;
  their presence does not establish that the legacy portals use them.

  Schedule, excuses, groups, and exclusion results were supplied on
  2026-10-27. The default skips them. Set @IncludeAlreadyReceived = 1 to
  repeat them.
  Save each remaining result set separately, with its column headers, and
  transfer securely: the results contain student identifiers and comments.
  Do not commit data exports. This script makes NO data changes or backups.
*/

USE [CAMs_Enterprise];
GO
SET NOCOUNT ON;

DECLARE @TermCalendarID int = 268;
DECLARE @IncludeAlreadyReceived bit = 0;

IF @IncludeAlreadyReceived = 1
BEGIN
    SELECT N'CAMs_Enterprise.dbo.ChapelSchedule' AS SourceTable,
           ChapelScheduleID, TermCalendarID, [Date], TimeFrom, TimeTo,
           DormDevo, DayOfWeek
    FROM dbo.ChapelSchedule
    WHERE TermCalendarID = @TermCalendarID
    ORDER BY [Date], ChapelScheduleID;

    SELECT N'CAMs_Enterprise.dbo.ChapelExcuses' AS SourceTable,
           ChapelExcusesID, TermCalendarID, StudentUID, Comment,
           Lastname, Firstname, Middle, Mon, Tue, Wed, Thu, Fri
    FROM dbo.ChapelExcuses
    WHERE TermCalendarID = @TermCalendarID
    ORDER BY StudentUID, ChapelExcusesID;
END;

IF @IncludeAlreadyReceived = 1
BEGIN
    -- Neither legacy table has a term column, so export every row.
    SELECT N'CAMs_Enterprise.dbo.ChapelGroups' AS SourceTable, *
    FROM dbo.ChapelGroups
    ORDER BY StudentUID, ChapelGroupsID;

    IF OBJECT_ID(N'dbo.Chapel_ExclusionList', N'U') IS NOT NULL
        EXEC (N'SELECT N''CAMs_Enterprise.dbo.Chapel_ExclusionList'' AS SourceTable,
                       StudentUID
                FROM dbo.Chapel_ExclusionList ORDER BY StudentUID;');
    ELSE IF OBJECT_ID(N'dbo.SAGU_Chapel_ExclusionList', N'U') IS NOT NULL
        EXEC (N'SELECT N''CAMs_Enterprise.dbo.SAGU_Chapel_ExclusionList'' AS SourceTable,
                       StudentUID
                FROM dbo.SAGU_Chapel_ExclusionList ORDER BY StudentUID;');
    ELSE
        PRINT 'No Chapel exclusion-list table found in CAMs_Enterprise.';
END;

-- The CAMS request tables use SAGU_ prefixes. Export those first. If a
-- request table lacks TermCalendarID, report that instead of exporting rows
-- from all terms. The unprefixed Integration table is only a fallback.
IF OBJECT_ID(N'dbo.SAGU_ChapelReleaseRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SAGU_ChapelReleaseRequests', N'TermCalendarID') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''CAMs_Enterprise.dbo.SAGU_ChapelReleaseRequests'' AS SourceTable, *
          FROM dbo.SAGU_ChapelReleaseRequests
          WHERE TermCalendarID = @TermCalendarID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE IF OBJECT_ID(N'dbo.SAGU_ChapelReleaseRequests', N'U') IS NOT NULL
    PRINT 'SAGU_ChapelReleaseRequests exists but has no TermCalendarID; review its schema before exporting.';
ELSE IF OBJECT_ID(N'dbo.ChapelReleaseRequests', N'U') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''CAMs_Enterprise.dbo.ChapelReleaseRequests'' AS SourceTable,
                 StudentUID, TermCalendarID, BeginDate, ExcuseType, Reason,
                 Mon, Tue, Wed, Thu, Fri, EmployerName, EmployerPhone, EmployerEmail
          FROM dbo.ChapelReleaseRequests
          WHERE TermCalendarID = @TermCalendarID
          ORDER BY StudentUID, BeginDate;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE IF DB_ID(N'Nelson_ReportingAndIntegration') IS NOT NULL
    AND OBJECT_ID(N'Nelson_ReportingAndIntegration.dbo.ChapelReleaseRequests', N'U') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''Nelson_ReportingAndIntegration.dbo.ChapelReleaseRequests'' AS SourceTable,
                 StudentUID, TermCalendarID, BeginDate, ExcuseType, Reason,
                 Mon, Tue, Wed, Thu, Fri, EmployerName, EmployerPhone, EmployerEmail
          FROM Nelson_ReportingAndIntegration.dbo.ChapelReleaseRequests
          WHERE TermCalendarID = @TermCalendarID
          ORDER BY StudentUID, BeginDate;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE
    PRINT 'ChapelReleaseRequests was not visible in either database.';

IF OBJECT_ID(N'dbo.SAGU_ChapelExcuseRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SAGU_ChapelExcuseRequests', N'TermCalendarID') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''CAMs_Enterprise.dbo.SAGU_ChapelExcuseRequests'' AS SourceTable, *
          FROM dbo.SAGU_ChapelExcuseRequests
          WHERE TermCalendarID = @TermCalendarID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE IF OBJECT_ID(N'dbo.SAGU_ChapelExcuseRequests', N'U') IS NOT NULL
    PRINT 'SAGU_ChapelExcuseRequests exists but has no TermCalendarID; review its schema before exporting.';
ELSE
    PRINT 'No SAGU_ChapelExcuseRequests table found in CAMs_Enterprise.';

-- The legacy staff portal writes SAGU_ChapelBonusBank in CAMS. Export it if
-- present, and also export the separately supplied Nelson Integration table.
-- Keeping both result sets makes it possible to establish which is current.
IF OBJECT_ID(N'dbo.SAGU_ChapelBonusBank', N'U') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''CAMs_Enterprise.dbo.SAGU_ChapelBonusBank'' AS SourceTable, *
          FROM dbo.SAGU_ChapelBonusBank
          WHERE TermCalendarID = @TermCalendarID
          ORDER BY StudentUID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE
    PRINT 'No dbo.SAGU_ChapelBonusBank table found in CAMs_Enterprise.';

IF DB_ID(N'Nelson_ReportingAndIntegration') IS NOT NULL
   AND OBJECT_ID(N'Nelson_ReportingAndIntegration.dbo.ChapelBonusBank', N'U') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''Nelson_ReportingAndIntegration.dbo.ChapelBonusBank'' AS SourceTable,
                 ChapelBonusBankID, TermCalendarID, StudentUID,
                 Lastname, Firstname, Middle, [Count]
          FROM Nelson_ReportingAndIntegration.dbo.ChapelBonusBank
          WHERE TermCalendarID = @TermCalendarID
          ORDER BY StudentUID, ChapelBonusBankID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE
    PRINT 'Nelson_ReportingAndIntegration.dbo.ChapelBonusBank was not visible on this server.';

-- Metadata only; confirms the bonus-bank objects without assuming that one
-- name is a synonym or a copy of the other.
SELECT N'CAMs_Enterprise' AS DatabaseName, s.name AS SchemaName,
       o.name AS ObjectName, o.type_desc AS ObjectType
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.name IN (N'ChapelBonusBank', N'SAGU_ChapelBonusBank')
ORDER BY s.name, o.name;
