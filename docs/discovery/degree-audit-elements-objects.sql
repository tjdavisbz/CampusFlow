/*
    CampusFlow Degree Audit discovery

    Run against CAMs_Enterprise and return each result set. This is read-only.

    Legacy portal calls we need to replace:
      CAMSPortal.busGeneral.LookupStudentProgram
      CAMSPortal.busPortal.ReadStuAudReq
      CAMSPortal.busPortal.ReadStuAudGrp
      CAMSPortal.busGeneral.ReadStudentAudit
*/

USE [CAMs_Enterprise];
GO

SET NOCOUNT ON;

/* 1. Candidate SQL modules and data objects by name. */
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    o.object_id,
    o.create_date,
    o.modify_date
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.is_ms_shipped = 0
  AND (
      o.name LIKE '%DegreeAudit%'
      OR o.name LIKE '%StudentAudit%'
      OR o.name LIKE '%StuAud%'
      OR o.name LIKE '%AuditProgram%'
      OR o.name LIKE '%AuditDegree%'
  )
ORDER BY o.type_desc, s.name, o.name;

/* 2. SQL modules whose definitions reference the concepts used by the COM methods. */
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    CASE
        WHEN m.definition LIKE '%LookupStudentProgram%' THEN 'LookupStudentProgram'
        WHEN m.definition LIKE '%ReadStudentAudit%' THEN 'ReadStudentAudit'
        WHEN m.definition LIKE '%StudentAudReq%' OR m.definition LIKE '%StuAudReq%' THEN 'Student audit requirement'
        WHEN m.definition LIKE '%StudentAudGrp%' OR m.definition LIKE '%StuAudGrp%' THEN 'Student audit group'
        WHEN m.definition LIKE '%AuditProgramID%' THEN 'Audit program'
        ELSE 'Degree audit'
    END AS MatchReason,
    o.object_id
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE
    m.definition LIKE '%LookupStudentProgram%'
    OR m.definition LIKE '%ReadStudentAudit%'
    OR m.definition LIKE '%StudentAudReq%'
    OR m.definition LIKE '%StuAudReq%'
    OR m.definition LIKE '%StudentAudGrp%'
    OR m.definition LIKE '%StuAudGrp%'
    OR m.definition LIKE '%AuditProgramID%'
    OR m.definition LIKE '%AuditDegreeID%'
ORDER BY s.name, o.name;

/* 3. Parameters for candidate procedures/functions. */
;WITH CandidateObjects AS
(
    SELECT DISTINCT o.object_id
    FROM sys.objects o
    LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
    WHERE o.is_ms_shipped = 0
      AND (
          o.name LIKE '%DegreeAudit%'
          OR o.name LIKE '%StudentAudit%'
          OR o.name LIKE '%StuAud%'
          OR o.name LIKE '%AuditProgram%'
          OR o.name LIKE '%AuditDegree%'
          OR m.definition LIKE '%LookupStudentProgram%'
          OR m.definition LIKE '%ReadStudentAudit%'
          OR m.definition LIKE '%StudentAudReq%'
          OR m.definition LIKE '%StudentAudGrp%'
      )
)
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    p.parameter_id AS ParameterOrder,
    p.name AS ParameterName,
    TYPE_NAME(p.user_type_id) AS DataType,
    p.max_length AS MaxLength,
    p.precision AS [Precision],
    p.scale AS Scale,
    p.is_output AS IsOutput
FROM CandidateObjects c
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.parameters p ON p.object_id = o.object_id
ORDER BY s.name, o.name, p.parameter_id;

/* 4. Table/view columns used to persist the calculated audit. */
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    c.column_id AS ColumnOrder,
    c.name AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
JOIN sys.columns c ON c.object_id = o.object_id
WHERE o.is_ms_shipped = 0
  AND o.type IN ('U', 'V')
  AND (
      o.name LIKE '%DegreeAudit%'
      OR o.name LIKE '%StudentAudit%'
      OR o.name LIKE '%StuAud%'
      OR o.name LIKE '%AuditProgram%'
      OR o.name LIKE '%AuditDegree%'
  )
ORDER BY s.name, o.name, c.column_id;

/* 5. Definitions of the candidate stored procedures, functions, and views. */
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType,
    m.definition AS SqlDefinition
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.is_ms_shipped = 0
  AND (
      o.name LIKE '%DegreeAudit%'
      OR o.name LIKE '%StudentAudit%'
      OR o.name LIKE '%StuAud%'
      OR o.name LIKE '%AuditProgram%'
      OR o.name LIKE '%AuditDegree%'
      OR m.definition LIKE '%LookupStudentProgram%'
      OR m.definition LIKE '%ReadStudentAudit%'
      OR m.definition LIKE '%StudentAudReq%'
      OR m.definition LIKE '%StudentAudGrp%'
  )
ORDER BY s.name, o.name;
GO
