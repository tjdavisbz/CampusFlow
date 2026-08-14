/*
    CampusFlow Advisor Portal - routing field discovery

    Run in CAMs_Enterprise and return all result sets. This script is read-only.
    It is the focused follow-up to scheduler-portal-elements-objects.sql.
*/

SET NOCOUNT ON;

DECLARE @Objects TABLE
(
    SchemaName sysname NOT NULL,
    ObjectName sysname NOT NULL,
    Purpose nvarchar(300) NOT NULL
);

INSERT @Objects (SchemaName, ObjectName, Purpose)
VALUES
    ('dbo', 'CAMSUser', 'Advisor identity and email'),
    ('dbo', 'CAMS_StudentProgram_View', 'Primary program, major, and specialization'),
    ('dbo', 'CAMS_StudentDegree_View', 'Active degree information'),
    ('dbo', 'CAMS_StudentStatusUserDefined_View', 'Attendance-type override'),
    ('dbo', '3DTech_Registration_tblIntentToGraduate', 'Graduating-student routing'),
    ('dbo', '3DTech_Collegiate_tblStudentTerm', 'Custom term-level student data'),
    ('dbo', 'StudentStatus', 'Term attendance type and student level');

-- 1. Object inventory and view definitions.
SELECT
    requested.SchemaName,
    requested.ObjectName,
    requested.Purpose,
    ObjectType = objects.type_desc,
    Found = CASE WHEN objects.object_id IS NULL THEN 'MISSING' ELSE 'FOUND' END,
    SqlDefinition = modules.definition
FROM @Objects requested
LEFT JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
LEFT JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
LEFT JOIN sys.sql_modules modules
    ON modules.object_id = objects.object_id
ORDER BY requested.ObjectName;

-- 2. Exact columns used to build configurable routing rules and identify advisors.
SELECT
    SchemaName = schemas.name,
    ObjectName = objects.name,
    ColumnOrder = columns.column_id,
    ColumnName = columns.name,
    DataType = types.name,
    columns.max_length,
    columns.precision,
    columns.scale,
    columns.is_nullable
FROM @Objects requested
JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
JOIN sys.columns columns
    ON columns.object_id = objects.object_id
JOIN sys.types types
    ON types.user_type_id = columns.user_type_id
ORDER BY objects.name, columns.column_id;

-- 3. Keys/indexes clarify stable advisor and student identifiers.
SELECT
    SchemaName = schemas.name,
    ObjectName = objects.name,
    IndexName = indexes.name,
    indexes.is_unique,
    indexes.is_primary_key,
    KeyOrder = indexColumns.key_ordinal,
    ColumnName = columns.name,
    indexColumns.is_included_column
FROM @Objects requested
JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
JOIN sys.indexes indexes
    ON indexes.object_id = objects.object_id
JOIN sys.index_columns indexColumns
    ON indexColumns.object_id = indexes.object_id
   AND indexColumns.index_id = indexes.index_id
JOIN sys.columns columns
    ON columns.object_id = indexColumns.object_id
   AND columns.column_id = indexColumns.column_id
WHERE objects.type = 'U'
ORDER BY objects.name, indexes.name, indexColumns.key_ordinal, columns.name;

-- 4. Return only the current user's advisor identity match, not a broad user sample.
SELECT
    CAMSUserID,
    CAMSUser,
    FirstName,
    LastName,
    EmailAddress
FROM dbo.CAMSUser
WHERE LOWER(LTRIM(RTRIM(EmailAddress))) = LOWER('terryjdavis@my.nelson.edu');
