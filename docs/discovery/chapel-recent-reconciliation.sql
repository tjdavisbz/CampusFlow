/*
  Chapel migration: one-event, student-matched scan reconciliation — READ ONLY.
  Run in CAMs_Enterprise and send the single aggregate result grid.
  No names, student IDs, or free-text comments leave the database.
  Default event: Fall 2026, Friday October 23 at 10:15 a.m.
  This compares the database's retained raw scans with current attendance,
  which may differ from the separately supplied CBORD CSV snapshot.
*/
USE [CAMs_Enterprise];
GO

DECLARE @TermId int = 268;
DECLARE @EventDate date = '20261023';
DECLARE @EventStart datetime2(3);
DECLARE @EventEnd datetime2(3);
DECLARE @ScheduleRows int;

SELECT
    @ScheduleRows = COUNT(*),
    @EventStart = MAX(DATETIME2FROMPARTS(YEAR(CS.[Date]), MONTH(CS.[Date]), DAY(CS.[Date]),
        DATEPART(HOUR, CS.TimeFrom), DATEPART(MINUTE, CS.TimeFrom), DATEPART(SECOND, CS.TimeFrom), 0, 3)),
    @EventEnd = MAX(DATETIME2FROMPARTS(YEAR(CS.[Date]), MONTH(CS.[Date]), DAY(CS.[Date]),
        DATEPART(HOUR, CS.TimeTo), DATEPART(MINUTE, CS.TimeTo), DATEPART(SECOND, CS.TimeTo), 0, 3))
FROM dbo.ChapelSchedule AS CS
WHERE CS.TermCalendarID = @TermId AND CAST(CS.[Date] AS date) = @EventDate;

IF @ScheduleRows <> 1
    THROW 51000, 'Expected exactly one scheduled chapel event on this term/date.', 1;

;WITH ScanTimes AS
(
    SELECT
        LTRIM(RTRIM(OCS.ID_Number)) AS StudentID,
        MIN(CASE WHEN OCS.Date_Time BETWEEN DATEADD(MINUTE, -20, @EventStart)
                                      AND DATEADD(MINUTE,   5, @EventStart)
            THEN OCS.Date_Time END) AS FirstEarlyScan,
        MIN(CASE WHEN OCS.Date_Time BETWEEN DATEADD(MINUTE, 6, @EventStart) AND @EventEnd
            THEN OCS.Date_Time END) AS FirstLateScan,
        MAX(CASE WHEN OCS.Date_Time BETWEEN DATEADD(MINUTE, -30, @EventEnd)
                                      AND DATEADD(HOUR, 1, @EventEnd)
            THEN OCS.Date_Time END) AS LastExitScan
    FROM dbo.SAGU_Odyssey_ChapelScans AS OCS
    WHERE OCS.Date_Time >= DATEADD(MINUTE, -20, @EventStart)
      AND OCS.Date_Time <= DATEADD(HOUR, 1, @EventEnd)
    GROUP BY LTRIM(RTRIM(OCS.ID_Number))
), Compared AS
(
    SELECT
        CASE
            WHEN CA.Present = 1 THEN 'Present'
            WHEN CA.Excused = 1 THEN 'Excused'
            WHEN CA.Absent = 1 THEN 'Absent'
            ELSE 'Pending'
        END AS LegacyStatus,
        CASE
            WHEN ST.FirstEarlyScan IS NOT NULL AND ST.LastExitScan > ST.FirstEarlyScan THEN 'Present candidate'
            WHEN ST.FirstLateScan IS NOT NULL AND ST.LastExitScan > ST.FirstLateScan THEN 'Late candidate'
            WHEN (ST.FirstEarlyScan IS NOT NULL OR ST.FirstLateScan IS NOT NULL)
                 AND ST.LastExitScan IS NOT NULL THEN 'Ambiguous overlap'
            WHEN ST.FirstEarlyScan IS NOT NULL THEN 'Early scan only'
            ELSE 'No qualifying pair'
        END AS ScanOutcome,
        CASE
            WHEN CA.Comment = 'Late' THEN 'Late'
            WHEN CA.Comment = 'Scan In Only' THEN 'Scan In Only'
            WHEN CA.Comment = 'Bonus Chapel' THEN 'Bonus Chapel'
            WHEN NULLIF(LTRIM(RTRIM(CA.Comment)), '') IS NULL THEN 'Blank'
            ELSE 'Other comment'
        END AS CommentCategory,
        CASE WHEN S.StudentID IS NULL THEN 1 ELSE 0 END AS MissingStudentID
    FROM dbo.ChapelAttendance AS CA
    LEFT JOIN dbo.Student AS S ON S.StudentUID = CA.StudentUID
    LEFT JOIN ScanTimes AS ST ON ST.StudentID = LTRIM(RTRIM(S.StudentID))
    WHERE CA.TermCalendarID = @TermId AND CA.CADate = @EventStart
)
SELECT LegacyStatus, ScanOutcome, CommentCategory, MissingStudentID, COUNT(*) AS StudentCount
FROM Compared
GROUP BY LegacyStatus, ScanOutcome, CommentCategory, MissingStudentID
ORDER BY LegacyStatus, ScanOutcome, CommentCategory, MissingStudentID;
