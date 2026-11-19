/*
CF-028: READ-ONLY legacy recovery export. Run in SSMS against windb3 / CAMS_Enterprise.

1. Use Results to Grid. In Query > Query Options > Results > Grid, set the
   maximum XML data retrieved to Unlimited before running.
2. Leave @StudentID = NULL for the full export, or enter one institutional StudentID.
3. Run the whole script. Click the single XML result link, then save that XML
   document as CF028-legacy-recovery.xml in a private, access-controlled folder.
   Save the XML document itself, not a screenshot or a copied grid cell.

CONFIDENTIAL: the XML contains saved security answers in readable form. Keep it
private; do not email it, paste it into chat, or save it inside a Git checkout.
CampusFlow will hash valid answers during the controlled import. This script does
not change CAMS, Elements, Microsoft365, AD, or any password. No credentials are
embedded. No SSNs, birthdates, names or portal passwords are selected.
*/
USE [CAMS_Enterprise];
GO
SET NOCOUNT ON;
SET LOCK_TIMEOUT 15000;

DECLARE @StudentID nvarchar(50) = NULL; -- NULL = all; otherwise institutional StudentID.
DECLARE @ExpectedStudentCount bigint;
DECLARE @FerpaAvailable bit;
DECLARE @DatabaseCollation nvarchar(128);
DECLARE @AnswerCollation nvarchar(128);
DECLARE @EligibilitySql nvarchar(max);
DECLARE @ExportSql nvarchar(max);

IF DB_NAME() <> N'CAMS_Enterprise'
BEGIN
    RAISERROR ('CF-028 stopped: select CAMS_Enterprise before running.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.Student', N'U') IS NULL
   OR OBJECT_ID(N'dbo.SAGU_Student_SecretQuestion', N'U') IS NULL
   OR OBJECT_ID(N'dbo.SAGU_SecretQuestions', N'U') IS NULL
BEGIN
    RAISERROR ('CF-028 stopped: a required legacy table is missing or not visible.', 16, 1);
    RETURN;
END;

SET @DatabaseCollation = CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), N'Collation'));
SELECT @AnswerCollation = collation_name
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.SAGU_Student_SecretQuestion') AND name = N'Answer';

IF @DatabaseCollation IS NULL OR @AnswerCollation IS NULL
BEGIN
    RAISERROR ('CF-028 stopped: the legacy answer comparison metadata is unavailable.', 16, 1);
    RETURN;
END;

SELECT @ExpectedStudentCount = COUNT_BIG(*)
FROM dbo.Student s
WHERE s.StudentID IS NOT NULL AND LTRIM(RTRIM(s.StudentID)) <> N''
  AND (@StudentID IS NULL OR LTRIM(RTRIM(s.StudentID)) = @StudentID);

IF @ExpectedStudentCount < 1 OR @ExpectedStudentCount > 100000
BEGIN
    RAISERROR ('CF-028 stopped: selection is empty or exceeds 100000 students.', 16, 1);
    RETURN;
END;

SET @FerpaAvailable = CASE
    WHEN OBJECT_ID(N'dbo.FERPA') IS NOT NULL
     AND OBJECTPROPERTY(OBJECT_ID(N'dbo.FERPA'), N'IsScalarFunction') = 1
    THEN 1 ELSE 0 END;

-- Only the exact legacy yes/no predicate is exported, never the FERPA return value.
-- If that function is absent, no-question records fail closed. Dynamic SQL avoids
-- compiling a reference to a missing function. All user input stays parameterized.
SET @EligibilitySql = N'CAST(0 AS bit)';
IF @FerpaAvailable = 1
    SET @EligibilitySql = N'CAST(CASE WHEN dbo.FERPA(s.StudentUID) <> -9999999 THEN 1 ELSE 0 END AS bit)';

SET @ExportSql = N'
SELECT
    1 AS [@FormatVersion],
    N''CampusFlow.LegacyRecoverySourceXml'' AS [@Kind],
    CONVERT(nvarchar(128), SERVERPROPERTY(N''ServerName'')) AS [LegacyServer],
    DB_NAME() AS [LegacyDatabase],
    CONVERT(nvarchar(30), GETUTCDATE(), 126) AS [ExportedAtUtc],
    @DatabaseCollation AS [DatabaseCollation],
    @AnswerCollation AS [AnswerCollation],
    @FerpaAvailable AS [FerpaFunctionAvailable],
    @ExpectedStudentCount AS [ExpectedStudentCount],
    CASE WHEN @StudentID IS NULL THEN N''all'' ELSE N''single-student'' END AS [Selection],
    (
        SELECT
            s.StudentUID AS [LegacyStudentUid],
            LTRIM(RTRIM(s.StudentID)) AS [StudentId],
            (SELECT COUNT_BIG(*) FROM dbo.SAGU_Student_SecretQuestion counted
             WHERE counted.StudentUID = s.StudentUID) AS [StoredQuestionCount],
            CASE WHEN NOT EXISTS
                (SELECT 1 FROM dbo.SAGU_Student_SecretQuestion existing
                 WHERE existing.StudentUID = s.StudentUID)
                THEN ' + @EligibilitySql + N'
                ELSE CAST(0 AS bit) END AS [LegacyNoQuestionAllowed],
            (
                SELECT TOP (2)
                    stored.QuestionID AS [QuestionId],
                    CONVERT(nvarchar(max), questions.Question) AS [Text],
                    CONVERT(nvarchar(max), stored.Answer) AS [Answer]
                FROM dbo.SAGU_Student_SecretQuestion stored
                LEFT JOIN dbo.SAGU_SecretQuestions questions
                    ON questions.QuestionID = stored.QuestionID
                WHERE stored.StudentUID = s.StudentUID
                ORDER BY stored.QuestionID
                FOR XML PATH(''Question''), TYPE
            ) AS [Questions]
        FROM dbo.Student s
        WHERE s.StudentID IS NOT NULL AND LTRIM(RTRIM(s.StudentID)) <> N''''
          AND (@StudentID IS NULL OR LTRIM(RTRIM(s.StudentID)) = @StudentID)
        ORDER BY s.StudentUID
        FOR XML PATH(''Student''), TYPE
    ) AS [Students]
FOR XML PATH(''CampusFlowRecoveryExport''), TYPE;';

EXEC sys.sp_executesql @ExportSql,
    N'@StudentID nvarchar(50), @DatabaseCollation nvarchar(128), @AnswerCollation nvarchar(128), @FerpaAvailable bit, @ExpectedStudentCount bigint',
    @StudentID = @StudentID,
    @DatabaseCollation = @DatabaseCollation,
    @AnswerCollation = @AnswerCollation,
    @FerpaAvailable = @FerpaAvailable,
    @ExpectedStudentCount = @ExpectedStudentCount;
