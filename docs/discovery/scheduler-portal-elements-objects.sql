/*
    CampusFlow Advisor Portal - legacy Scheduler Portal discovery

    Run in CAMs_Enterprise and return all result sets. This script is read-only.
    It captures the custom objects that contain the legacy queue, course-review,
    comment, and notification-selection behavior needed for the new portal.
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
    ('dbo', 'SAGU_SchedulerStudentSelection', 'Advisor queue and advisor-to-student assignment rules'),
    ('dbo', 'SAGU_PortalSelectedCourses', 'Pending courses presented for advisor action'),
    ('dbo', 'SAGU_OnlineRegCurrentSchedule', 'Previously finalized course decisions and comments'),
    ('dbo', 'SAGU_ProccessHoldingTankCourses', 'Accept/reject processing and rejected-course removal behavior'),
    ('dbo', 'SAGU_UpdateSchedulerComments', 'Overall advisor comment persistence'),
    ('dbo', 'SAGU_HoldTankProcessedCoursesInfo', 'Approved/rejected course details used in student notifications'),
    ('dbo', 'SAGU_OnlineRegComments', 'Legacy student and advisor overall-comment storage'),
    ('dbo', '3DTech_Collegiate_tblEnrollments', 'Legacy per-course review flags and comments'),
    ('dbo', 'CAMS_SRAcademicNarrative_View', 'Student registration questions/comments'),
    ('dbo', 'CAMSUser', 'Advisor identity, email, and Elements user identifier');

-- 1. Inventory and existence.
SELECT
    requested.SchemaName,
    requested.ObjectName,
    requested.Purpose,
    object_id = objects.object_id,
    ObjectType = objects.type_desc,
    Found = CASE WHEN objects.object_id IS NULL THEN 'MISSING' ELSE 'FOUND' END,
    objects.create_date,
    objects.modify_date
FROM @Objects requested
LEFT JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
LEFT JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
ORDER BY requested.ObjectName;

-- 2. Complete SQL definitions for programmable objects.
SELECT
    SchemaName = schemas.name,
    ObjectName = objects.name,
    ObjectType = objects.type_desc,
    SqlDefinition = modules.definition
FROM @Objects requested
JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
LEFT JOIN sys.sql_modules modules
    ON modules.object_id = objects.object_id
ORDER BY objects.name;

-- 3. Parameters for procedures and functions.
SELECT
    SchemaName = schemas.name,
    ObjectName = objects.name,
    ParameterOrder = parameters.parameter_id,
    ParameterName = parameters.name,
    DataType = types.name,
    parameters.max_length,
    parameters.precision,
    parameters.scale,
    parameters.is_output,
    parameters.has_default_value,
    DefaultValue = parameters.default_value
FROM @Objects requested
JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
JOIN sys.parameters parameters
    ON parameters.object_id = objects.object_id
JOIN sys.types types
    ON types.user_type_id = parameters.user_type_id
WHERE parameters.parameter_id > 0
ORDER BY objects.name, parameters.parameter_id;

-- 4. Result-set metadata where SQL Server can determine it.
SELECT
    requested.SchemaName,
    requested.ObjectName,
    ColumnOrder = result.column_ordinal,
    ColumnName = result.name,
    DataType = result.system_type_name,
    result.is_nullable,
    result.error_number,
    result.error_message
FROM @Objects requested
JOIN sys.schemas schemas
    ON schemas.name = requested.SchemaName
JOIN sys.objects objects
    ON objects.schema_id = schemas.schema_id
   AND objects.name = requested.ObjectName
CROSS APPLY sys.dm_exec_describe_first_result_set_for_object(objects.object_id, 0) result
WHERE objects.type IN ('P', 'IF', 'TF', 'FN')
ORDER BY requested.ObjectName, result.column_ordinal;

-- 5. Columns for custom tables and views used directly by the legacy portal.
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
WHERE objects.type IN ('U', 'V')
ORDER BY objects.name, columns.column_id;

-- 6. Direct dependencies of the requested programmable objects.
SELECT DISTINCT
    ReferencingSchema = referencingSchema.name,
    ReferencingObject = referencingObject.name,
    ReferencedDatabase = dependencies.referenced_database_name,
    ReferencedSchema = dependencies.referenced_schema_name,
    ReferencedObject = dependencies.referenced_entity_name,
    ReferencedObjectType = referencedObject.type_desc,
    dependencies.is_ambiguous
FROM @Objects requested
JOIN sys.schemas referencingSchema
    ON referencingSchema.name = requested.SchemaName
JOIN sys.objects referencingObject
    ON referencingObject.schema_id = referencingSchema.schema_id
   AND referencingObject.name = requested.ObjectName
JOIN sys.sql_expression_dependencies dependencies
    ON dependencies.referencing_id = referencingObject.object_id
LEFT JOIN sys.objects referencedObject
    ON referencedObject.object_id = dependencies.referenced_id
ORDER BY referencingObject.name, dependencies.referenced_entity_name;

-- 7. Indexes and keys on the two legacy workflow tables and CAMSUser.
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

-- 8. Small, non-sensitive value samples needed to understand advisor identity.
SELECT TOP (20)
    CAMSUserID,
    CAMSUser,
    FirstName,
    LastName,
    EmailAddress
FROM dbo.CAMSUser
WHERE NULLIF(LTRIM(RTRIM(EmailAddress)), '') IS NOT NULL
ORDER BY CAMSUserID DESC;
