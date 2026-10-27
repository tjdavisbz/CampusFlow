namespace CampusFlow.Bibliu;

internal static class BibliuExportSql
{
    public const string Courses = """
        SELECT @Store StoreNumber,
          CASE WHEN tc.TextTerm LIKE 'Fall%' THEN 'F' WHEN tc.TextTerm LIKE 'Spring%' THEN 'S'
               WHEN tc.TextTerm LIKE 'Summer%' THEN 'A' ELSE 'X' END TermCode,
          RIGHT(YEAR(tc.TermStartDate),2) TermYear,
          s.Department, s.Course, s.Section, s.Instructor,
          s.MaximumEnroll EstimatedEnrollment, '99999' EstimatePreEnrollment,
          s.TotalEnrolled ActualEnrollment, ' ' ContinuationClass, ' ' EveningClass,
          ' ' ExtensionClass, ' ' NoTextFlag, '   ' [Location#], s.CourseName CourseTitle,
          s.SROfferID CourseID
        FROM dbo.CAMS_SROffer_View s
        JOIN dbo.TermCalendar tc ON tc.TermCalendarID=s.SemesterID
        WHERE tc.TermCalendarID=@Bibliu
        ORDER BY TermCode,TermYear,s.Department,s.Course,s.Section,s.Instructor,s.SROfferID;
        """;
    public const string Schedule = """
        SELECT DISTINCT s.SROfferID CRN, s.TextTerm Term, s.Department DepartmentCode,
          s.Department, s.CourseName, s.Course CourseCode, s.Section SectionCode,
          CONVERT(varchar(10),s.StartDate,101) StartDate,
          CASE WHEN CONVERT(varchar(10),s.EndDate,101)='12/09/2025' THEN '12/12/2025'
               ELSE CONVERT(varchar(10),s.EndDate,101) END EndDate,
          '' CensusDate, s.MaximumEnroll Capacity, s.TotalEnrolled ActiveEnrollments,
          CASE WHEN s.Status='open' THEN 'Active' ELSE 'Inactive' END Status,
          f.FirstName ProfessorFirstName, f.LastName ProfessorLastName, a.Email1 ProfessorEmailAddress
        FROM dbo.CAMS_SROffer_View s
        LEFT JOIN dbo.CAMS_SROfferSchedule_Top1Faculty_View fv ON fv.SROfferID=s.SROfferID
        LEFT JOIN dbo.Faculty f ON f.FacultyID=fv.FacultyID
        LEFT JOIN dbo.CAMS_FacultyAddressList_View a ON a.FacultyID=f.FacultyID
          AND a.ActiveFlag='Yes' AND a.AddressType='Home'
        WHERE s.TextTerm IN (SELECT [value] FROM OPENJSON(@Expanded))
        ORDER BY Term,DepartmentCode,CourseCode,SectionCode,CRN;
        """;
    public const string Enrollments = """
        WITH registrations AS (
          SELECT StudentUID,StudentID,Term,SROfferID,Department,Course,Section,Grade,CollegeID
          FROM dbo.CAMS_SRAcademic_View
          UNION ALL
          SELECT StudentUID,StudentID,Term,SROfferID,Department,CourseID,Section,'W',CollegeID
          FROM dbo.CAMS_DropBag_View
        )
        SELECT DISTINCT r.StudentID,s.LastName,s.FirstName,a.Email1 EmailAddress,r.Term,
          o.CourseName CourseTitle,r.Department+'-'+r.Course CourseCode,
          r.Department+'-'+r.Course+'-'+r.Section CourseAndSectionCode,
          r.Course CourseSection,r.Section SectionCode,
          CONVERT(varchar(10),o.StartDate,101) StartDate,
          CASE WHEN CONVERT(varchar(10),o.EndDate,101)='12/09/2025' THEN '12/12/2025'
               ELSE CONVERT(varchar(10),o.EndDate,101) END EndDate,
          '' CensusDate,CASE WHEN r.Grade LIKE 'W%' THEN 'Inactive' ELSE 'Active' END EnrollmentStatus,
          '' OptOutDate
        FROM registrations r
        JOIN dbo.CAMS_SROffer_View o ON o.SROfferID=r.SROfferID
        JOIN dbo.CAMS_Student_View s ON s.StudentUID=r.StudentUID
        JOIN dbo.CAMS_StudentAddressList_View a ON a.StudentUID=r.StudentUID
          AND a.ActiveFlag='Yes' AND a.AddressType='Local'
        WHERE r.CollegeID=0 AND r.Term IN (SELECT [value] FROM OPENJSON(@Expanded))
        ORDER BY r.StudentID,Term,CourseAndSectionCode,EnrollmentStatus;
        """;
    public const string Users = """
        WITH candidates AS (
          SELECT DISTINCT s.StudentUID,s.StudentID,s.LastName,s.FirstName,1 IdentityRecord,0 VoucherRecord
          FROM dbo.CAMS_StudentStatus_View ss
          JOIN dbo.Student s ON s.StudentUID=ss.StudentUID
          WHERE ss.TermCalendarID=@Bibliu AND ISNUMERIC(s.StudentID)=1
            AND EXISTS (SELECT 1 FROM dbo.CAMS_StudentAddressList_View a
              WHERE a.StudentUID=s.StudentUID AND a.ActiveFlag='Yes' AND a.AddressType='Home')
          UNION ALL
          SELECT DISTINCT s.StudentUID,s.StudentID,s.LastName,s.FirstName,0 IdentityRecord,1 VoucherRecord
          FROM dbo.CAMS_StudentStatus_View ss
          JOIN dbo.CAMS_Student_View s ON s.StudentUID=ss.StudentUID
          WHERE ss.TermCalendarID=@Billing AND ISNUMERIC(s.StudentID)=1
            AND EXISTS (SELECT 1 FROM OPENJSON(@Approved) ap WHERE ap.[value]=CONVERT(varchar(64),s.StudentUID))
            AND EXISTS (SELECT 1 FROM dbo.CAMS_StudentAddressList_View a
              WHERE a.StudentUID=s.StudentUID AND a.ActiveFlag='Yes' AND a.AddressType='Home')
        ), students AS (
          SELECT StudentUID,StudentID,LastName,FirstName,MAX(IdentityRecord) IdentityRecord,MAX(VoucherRecord) VoucherRecord
          FROM candidates GROUP BY StudentUID,StudentID,LastName,FirstName
        )
        SELECT s.StudentID,s.LastName,s.FirstName,s.IdentityRecord,s.VoucherRecord,
          CASE WHEN EXISTS(SELECT 1 FROM dbo.CAMS_StudentHolds_View h WHERE h.StudentUID=s.StudentUID
            AND h.HoldCategory='Voc Rehab' AND h.HoldStatus='Hold') THEN 1 ELSE 0 END RehabHold,
          ISNULL(ledger.Balance,0)+ISNULL(batch.Balance,0)-ISNULL(aid.PendingAid,0) Balance,
          ISNULL(ledger.Used,0)+ISNULL(batch.Used,0) Used
        FROM students s
        JOIN dbo.TermCalendar target ON target.TermCalendarID=@Billing
        OUTER APPLY (
          SELECT SUM(CASE WHEN td.ReportFlag='Yes' AND b.Voided='No'
            AND (b.TermCalendarID=@Billing OR (tc.Term<bt.Term AND b.Reversing='No'))
            THEN b.ShowAmount ELSE 0 END) Balance,
            SUM(CASE WHEN b.TermCalendarID=@Billing AND td.TransDoc='BOOK' THEN b.ShowAmount ELSE 0 END) Used
          FROM dbo.Billing b JOIN dbo.Transdoc td ON td.TransDocID=b.TransDocID
          JOIN dbo.TermCalendar tc ON tc.TermCalendarID=b.TermCalendarID
          JOIN dbo.TermCalendar bt ON bt.TermCalendarID=@Billing
          WHERE b.OwnerUID=s.StudentUID AND s.VoucherRecord=1
        ) ledger
        OUTER APPLY (
          SELECT SUM(CASE WHEN td.ReportFlag='Yes' AND (b.TermCalendarID=@Billing OR tc.Term<bt.Term)
            THEN b.ShowAmount ELSE 0 END) Balance,
            SUM(CASE WHEN b.TermCalendarID=@Billing AND td.TransDoc='BOOK' THEN b.ShowAmount ELSE 0 END) Used
          FROM dbo.BillingBatch b JOIN dbo.Transdoc td ON td.TransDocID=b.TransDocID
          JOIN dbo.TermCalendar tc ON tc.TermCalendarID=b.TermCalendarID
          JOIN dbo.TermCalendar bt ON bt.TermCalendarID=@Billing
          WHERE b.OwnerUID=s.StudentUID AND s.VoucherRecord=1
        ) batch
        OUTER APPLY (
          SELECT SUM(fa.EstimatedAmount) PendingAid
          FROM dbo.CAMS_FinancialAward_View fa
          JOIN dbo.CAMS_FinancialAwardRef_View ar ON ar.AwardTypeID=fa.TransTypeID
          WHERE fa.StudentUID=s.StudentUID AND fa.TermCalendarID=@Billing AND s.VoucherRecord=1
            AND fa.EstimatedAmount<>0 AND ar.ShowOnBillStatement='Yes' AND fa.SentToBilling<>'Yes'
            AND ((ar.AwardType='TEG' AND fa.AwardStatus='Guarantee')
              OR (ar.IsALoan<>'Yes' AND ar.AwardType<>'TEG')
              OR (fa.StudentAccepted=1 AND ar.IsALoan='Yes'))
        ) aid
        WHERE s.IdentityRecord=1 OR s.VoucherRecord=1
        ORDER BY s.StudentID;
        """;
}
