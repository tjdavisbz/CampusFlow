/*
  Read-only CAMS Chapel request follow-up.
  Confirms the Fall 2026 release-request count. The four student-portal
  procedure definitions were supplied separately on 2026-10-27.
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
                 @TermCalendarID AS TermCalendarID, COUNT_BIG(*) AS RequestCount
          FROM dbo.SAGU_ChapelReleaseRequests
          WHERE TermCalendarID = @TermCalendarID;',
        N'@TermCalendarID int', @TermCalendarID = @TermCalendarID;
ELSE
    PRINT 'SAGU_ChapelReleaseRequests or its TermCalendarID column was not visible in CAMs_Enterprise.';
