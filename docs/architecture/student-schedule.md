# Student schedule

CampusFlow exposes course schedules through the provider-neutral
`IStudentInformationSystemScheduleLookup` boundary. The Thesis Elements implementation uses
`dbo.CAMS_StudentRegisterScheduleDetail_View`, which combines the academic registration
represented by `CAMS_SRAcademic_View` with offering schedule, room, and instructor data.

Queries use the authenticated student's linked `StudentUID` as a SQL parameter. The public
student ID remains the only SIS identifier shown in the UI.

The normalized course model includes term identifiers, department and course number, course
type, section, title, credits, registration status, course dates, instructor, meeting days,
meeting times, and room. Thesis Elements represents online courses with `N\\A` meeting days
and midnight sentinel times. CampusFlow normalizes missing or sentinel meeting data to
**Online** and does not display a room for those courses.

Historical course cards may display `NumberGradeMidTerm`, `NumberGradeFinal`, and the final
letter `Grade` from `CAMS_SRAcademic_View`. Grades are included only when
`ShowGradeReport = 'Yes'`; current and upcoming cards do not display grades.

The page uses the same term-context rule as Billing: the configured/current term and later
terms appear in Current & Upcoming Schedules; earlier terms appear as collapsed history. Only
the current term opens automatically. This is display organization and does not create a
global user-selected term.

Student course selection and the separate advisor review workflow are documented in
[`registration-and-advisor-review.md`](registration-and-advisor-review.md).
