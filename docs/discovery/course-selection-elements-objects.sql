/*
    CampusFlow Course Selection - Elements object discovery

    Purpose:
      Return definitions and metadata for the legacy SQL objects used by the
      OnlineRegistration course-selection workflow, plus their SQL dependencies.

    Safety:
      Read-only. This script does not create, alter, update, or delete anything.

    Run in:
      CAMs_Enterprise

    Return:
      Please save all result sets (Results to File is ideal) and send the output
      back to the CampusFlow development team.
*/

USE [CAMs_Enterprise];
SET NOCOUNT ON;

DECLARE @RequestedObjects table
(
    SchemaName sysname NOT NULL,
    ObjectName sysname NOT NULL,
    Purpose nvarchar(250) NOT NULL,
    PRIMARY KEY (SchemaName, ObjectName)
);

INSERT @RequestedObjects (SchemaName, ObjectName, Purpose)
VALUES
    (N'dbo', N'3DTech_Collegiate_getCourseOffering',  N'Load one offered section'),
    (N'dbo', N'3DTech_Collegiate_getCourseOfferings', N'Load sections available for a term'),
    (N'dbo', N'3DTech_Collegiate_getMasterCourse',    N'Load course catalog details'),
    (N'dbo', N'3DTech_Collegiate_getMasterCourses',   N'Load course catalog records'),
    (N'dbo', N'3DTech_Collegiate_getEnrollments',     N'Load a student''s selected/enrolled courses'),
    (N'dbo', N'3DTech_Collegiate_saveEnrollment',     N'Legacy add/update enrollment behavior'),
    (N'dbo', N'3DTech_Collegiate_deleteEnrollment',   N'Legacy drop behavior'),
    (N'dbo', N'3DTech_Collegiate_getStudentTerms',    N'Term eligibility, registration group, and credit limit'),
    (N'dbo', N'3DTech_Collegiate_getLookupTerm',      N'Load one academic term'),
    (N'dbo', N'3DTech_Collegiate_getLookupTerms',     N'Load academic terms'),
    (N'dbo', N'3DTech_Collegiate_getPerson',          N'Legacy student context and identifiers');

-- 1. Requested-object inventory. Missing objects are intentionally included.
SELECT
    r.SchemaName,
    r.ObjectName,
    r.Purpose,
    o.object_id AS ObjectId,
    o.type_desc AS ObjectType,
    CASE WHEN o.object_id IS NULL THEN N'MISSING' ELSE N'FOUND' END AS DiscoveryStatus,
    o.create_date AS CreateDate,
    o.modify_date AS ModifyDate
FROM @RequestedObjects r
LEFT JOIN sys.schemas s
    ON s.name = r.SchemaName
LEFT JOIN sys.objects o
    ON o.schema_id = s.schema_id
   AND o.name = r.ObjectName
ORDER BY r.ObjectName;

-- 2. Parameters for requested procedures/functions.
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    p.parameter_id AS ParameterOrder,
    p.name AS ParameterName,
    TYPE_NAME(p.user_type_id) AS DataType,
    p.max_length AS MaxLength,
    p.[precision] AS [Precision],
    p.scale AS Scale,
    p.is_output AS IsOutput,
    p.has_default_value AS HasDefaultValue,
    CONVERT(nvarchar(4000), p.default_value) AS DefaultValue
FROM @RequestedObjects r
JOIN sys.schemas s
    ON s.name = r.SchemaName
JOIN sys.objects o
    ON o.schema_id = s.schema_id
   AND o.name = r.ObjectName
JOIN sys.parameters p
    ON p.object_id = o.object_id
ORDER BY s.name, o.name, p.parameter_id;

-- 3. Full source definitions of requested SQL modules.
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    sm.uses_ansi_nulls AS UsesAnsiNulls,
    sm.uses_quoted_identifier AS UsesQuotedIdentifier,
    sm.is_schema_bound AS IsSchemaBound,
    sm.definition AS SqlDefinition
FROM @RequestedObjects r
JOIN sys.schemas s
    ON s.name = r.SchemaName
JOIN sys.objects o
    ON o.schema_id = s.schema_id
   AND o.name = r.ObjectName
LEFT JOIN sys.sql_modules sm
    ON sm.object_id = o.object_id
ORDER BY s.name, o.name;
-- 4. Direct dependencies named by the requested modules.
SELECT DISTINCT
    OBJECT_SCHEMA_NAME(d.referencing_id) AS ReferencingSchema,
    OBJECT_NAME(d.referencing_id) AS ReferencingObject,
    COALESCE(d.referenced_database_name, DB_NAME()) AS ReferencedDatabase,
    COALESCE(d.referenced_schema_name, OBJECT_SCHEMA_NAME(d.referenced_id)) AS ReferencedSchema,
    COALESCE(d.referenced_entity_name, OBJECT_NAME(d.referenced_id)) AS ReferencedObject,
    ro.type_desc AS ReferencedObjectType,
    d.is_ambiguous AS IsAmbiguous
FROM @RequestedObjects r
JOIN sys.schemas s
    ON s.name = r.SchemaName
JOIN sys.objects o
    ON o.schema_id = s.schema_id
   AND o.name = r.ObjectName
JOIN sys.sql_expression_dependencies d
    ON d.referencing_id = o.object_id
LEFT JOIN sys.objects ro
    ON ro.object_id = d.referenced_id
ORDER BY ReferencingSchema, ReferencingObject, ReferencedDatabase,
         ReferencedSchema, ReferencedObject;

-- 5. Column metadata for local tables/views directly referenced by the modules.
;WITH LocalDependencies AS
(
    SELECT DISTINCT d.referenced_id
    FROM @RequestedObjects r
    JOIN sys.schemas s
        ON s.name = r.SchemaName
    JOIN sys.objects o
        ON o.schema_id = s.schema_id
       AND o.name = r.ObjectName
    JOIN sys.sql_expression_dependencies d
        ON d.referencing_id = o.object_id
    WHERE d.referenced_id IS NOT NULL
      AND d.referenced_database_name IS NULL
)
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    c.column_id AS ColumnOrder,
    c.name AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType,
    c.max_length AS MaxLength,
    c.[precision] AS [Precision],
    c.scale AS Scale,
    c.is_nullable AS IsNullable,
    c.is_identity AS IsIdentity,
    dc.definition AS DefaultDefinition,
    cc.definition AS ComputedDefinition
FROM LocalDependencies d
JOIN sys.objects o
    ON o.object_id = d.referenced_id
JOIN sys.schemas s
    ON s.schema_id = o.schema_id
JOIN sys.columns c
    ON c.object_id = o.object_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id
   AND dc.parent_column_id = c.column_id
LEFT JOIN sys.computed_columns cc
    ON cc.object_id = c.object_id
   AND cc.column_id = c.column_id
WHERE o.type IN (N'U', N'V')
ORDER BY s.name, o.name, c.column_id;

-- 6. Definitions for directly referenced local views/functions/procedures.
;WITH LocalModuleDependencies AS
(
    SELECT DISTINCT d.referenced_id
    FROM @RequestedObjects r
    JOIN sys.schemas s
        ON s.name = r.SchemaName
    JOIN sys.objects o
        ON o.schema_id = s.schema_id
       AND o.name = r.ObjectName
    JOIN sys.sql_expression_dependencies d
        ON d.referencing_id = o.object_id
    WHERE d.referenced_id IS NOT NULL
      AND d.referenced_database_name IS NULL
)
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    sm.definition AS SqlDefinition
FROM LocalModuleDependencies d
JOIN sys.objects o
    ON o.object_id = d.referenced_id
JOIN sys.schemas s
    ON s.schema_id = o.schema_id
JOIN sys.sql_modules sm
    ON sm.object_id = o.object_id
ORDER BY s.name, o.name;
