/*
    CampusFlow Course Selection discovery

    Run this against the legacy CAMs_Enterprise database that contains
    dbo.SAGU_SectionAttendanceTypes. This is read-only.
*/

SET NOCOUNT ON;

SELECT
    sat.Section,
    sat.AttendanceTypeID,
    LTRIM(RTRIM(g.DisplayText)) AS AttendanceType
FROM dbo.SAGU_SectionAttendanceTypes AS sat
INNER JOIN dbo.Glossary AS g
    ON g.UniqueID = sat.AttendanceTypeID
ORDER BY
    sat.Section,
    g.DisplayText;
