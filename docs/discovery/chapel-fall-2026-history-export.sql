/*
  Fall 2026 chapel history export — READ ONLY.
  Run in CAMs_Enterprise. Save the result grid as a CSV file and share the file,
  rather than pasting tens of thousands of rows into chat.
  Contains student identifiers/names and staff comments: handle as FERPA data.
  Do not commit the exported CSV to Git.
  This is a point-in-time extract; take a fresh export at final cutover.
*/
USE [CAMs_Enterprise];
GO

SET NOCOUNT ON;

DECLARE @TermId int = 268;

SELECT
    CA.ChapelAttendanceID,
    CA.TermCalendarID,
    CA.CADate,
    CA.StudentUID AS LegacyStudentUID,
    LTRIM(RTRIM(S.StudentID)) AS StudentID,
    CA.Present,
    CA.Absent,
    CA.Excused,
    CA.Comment,
    CA.Lastname,
    CA.Firstname,
    CA.Middle,
    CA.DateOnly
FROM dbo.ChapelAttendance AS CA
LEFT JOIN dbo.Student AS S ON S.StudentUID = CA.StudentUID
WHERE CA.TermCalendarID = @TermId
ORDER BY CA.CADate, CA.StudentUID, CA.ChapelAttendanceID;
