/*
  Read-only CAMS Chapel request follow-up.
  Confirms the Fall 2026 release-request count and returns the four student
  portal stored-procedure definitions needed to port the request workflow.
  The definitions may reveal institution-specific logic; review before sharing.
  No tables, backups, or application data are changed.
*/

USE [CAMs_Enterprise];
GO
SET NOCOUNT ON;

DECLARE @TermCalendarID int = 268; -- Fall 2026

IF OBJECT_ID(N'dbo.SAGU_ChapelReleaseRequests', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SAGU_ChapelReleaseRequests', N'TermCalendarID') IS NOT NULL
    EXEC sys.sp_executesql
        N'SELECT N''CAMs_Enterprise.dbo.SAGU_ChapelReleaseRequests'' AS SourceTable,
                 @TermCalendarID AS TermCalendarID, COUNT_BIG(*) AS RowCount
          FROM dbo.SAGU_ChapelReleaseRequests
          WHERE TermCalendarID = @TermCalendarID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE
    PRINT 'SAGU_ChapelReleaseRequests or its TermCalendarID column was not visible in CAMs_Enterprise.';

SELECT s.name AS SchemaName, o.name AS ProcedureName,
       m.definition AS SqlDefinition
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.type IN (N'P', N'PC')
  AND o.name IN (
      N'SAGU_StudentPortal_GetChapelExcuse',
      N'SAGU_StudentPortal_PostChapelExcuse',
      N'SAGU_StudentPortal_GetChapelRelease',
      N'SAGU_StudentPortal_PostChapelRelease')
ORDER BY o.name;
