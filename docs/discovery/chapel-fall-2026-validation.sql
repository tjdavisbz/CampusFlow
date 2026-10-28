/*
  Chapel migration comparison export — READ ONLY.
  Run against the CAMs_Enterprise database used by the existing chapel procedures.
  Returns (1) Fall 2026 scheduled events and (2) per-event attendance counts.
  It does not create tables, back up data, or modify application data.
  Neither result includes student identifiers.
*/
USE [CAMs_Enterprise];
GO

DECLARE @TermId int;
DECLARE @TermMatches int;

SELECT @TermMatches = COUNT(*), @TermId = MAX(TermCalendarID)
FROM dbo.TermCalendar
WHERE TextTerm = 'Fall 2026';

IF @TermMatches <> 1
    THROW 51000, 'Expected exactly one Fall 2026 term in this database.', 1;

SELECT
    TC.TextTerm,
    CS.TermCalendarID,
    CS.ChapelScheduleID,
    CS.[Date],
    CS.TimeFrom,
    CS.TimeTo,
    CS.DormDevo,
    CS.DayOfWeek
FROM dbo.ChapelSchedule AS CS
INNER JOIN dbo.TermCalendar AS TC ON TC.TermCalendarID = CS.TermCalendarID
WHERE CS.TermCalendarID = @TermId
ORDER BY CS.[Date], CS.TimeFrom, CS.ChapelScheduleID;

SELECT
    CA.TermCalendarID,
    CA.CADate,
    COUNT(*) AS AttendanceRows,
    SUM(CASE WHEN CA.Present = 1 THEN 1 ELSE 0 END) AS PresentRows,
    SUM(CASE WHEN CA.Absent = 1 THEN 1 ELSE 0 END) AS AbsentRows,
    SUM(CASE WHEN CA.Excused = 1 THEN 1 ELSE 0 END) AS ExcusedRows,
    SUM(CASE WHEN ISNULL(CA.Present, 0) = 0
              AND ISNULL(CA.Absent, 0) = 0
              AND ISNULL(CA.Excused, 0) = 0 THEN 1 ELSE 0 END) AS PendingRows
FROM dbo.ChapelAttendance AS CA
WHERE CA.TermCalendarID = @TermId
GROUP BY CA.TermCalendarID, CA.CADate
ORDER BY CA.CADate;
