/* Run on the ELEMENTS READ-ONLY REPORTING REPLICA, not legacy CAMS.
   Source metadata only; no student rows, writes, or module execution.
   VIEW DEFINITION is needed for complete metadata. Save all result sets and
   preserve newlines in definition cells (prefer Results to File/XML extraction
   or native SSMS Generate Scripts when reviewing modules with line comments).
   Finding an object does not prove legacy-compatible row semantics. */
SET NOCOUNT ON;
DECLARE @Objects TABLE (ObjectName sysname PRIMARY KEY);
INSERT @Objects VALUES
 (N'TermCalendar'), (N'CAMS_Student_View'), (N'CAMS_StudentStatus_View'),
 (N'CAMS_StudentAddressList_View'), (N'CAMS_SROffer_View'),
 (N'CAMS_SRAcademic_View'), (N'CAMS_DropBag_View'),
 (N'CAMS_StudentRegisterScheduleDetail_View'),
 (N'CAMS_SROfferSchedule_Top1Faculty_View'), (N'Faculty'),
 (N'CAMS_FacultyAddressList_View'), (N'CAMS_StudentHolds_View'),
 (N'Billing'), (N'BillingBatch'), (N'BatchMaster'), (N'Transdoc'),
 (N'CAMS_FinancialAward_View'), (N'CAMS_FinancialAwardRef_View');

SELECT N'01 Context' AS ResultSet, DB_NAME() AS DatabaseName,
 HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS HasViewDefinition;

SELECT N'02 Required objects' AS ResultSet, n.ObjectName,
 SCHEMA_NAME(o.schema_id) AS SchemaName, o.type_desc,
 CASE WHEN o.object_id IS NULL THEN N'Missing or not visible'
      ELSE N'Found: verify semantics' END AS Status
FROM @Objects n
LEFT JOIN sys.objects o ON o.name = n.ObjectName AND o.schema_id = SCHEMA_ID(N'dbo')
ORDER BY n.ObjectName;

SELECT N'03 Columns' AS ResultSet, SCHEMA_NAME(o.schema_id) AS SchemaName,
 o.name AS ObjectName, c.column_id, c.name AS ColumnName,
 TYPE_NAME(c.user_type_id) AS DataType, c.max_length AS MaximumLengthBytes,
 c.precision, c.scale, c.is_nullable
FROM @Objects n
JOIN sys.objects o ON o.name = n.ObjectName AND o.schema_id = SCHEMA_ID(N'dbo')
JOIN sys.columns c ON c.object_id = o.object_id
ORDER BY o.name, c.column_id;

SELECT N'04 View definitions' AS ResultSet, SCHEMA_NAME(o.schema_id) AS SchemaName,
 o.name AS ObjectName, m.definition AS SqlDefinition,
 CONVERT(int, OBJECTPROPERTYEX(o.object_id, 'IsEncrypted')) AS IsEncrypted
FROM @Objects n
JOIN sys.objects o ON o.name = n.ObjectName AND o.schema_id = SCHEMA_ID(N'dbo')
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.type = 'V'
ORDER BY o.name;

SELECT N'05 Recorded dependencies' AS ResultSet,
 OBJECT_SCHEMA_NAME(d.referencing_id) AS SourceSchema,
 OBJECT_NAME(d.referencing_id) AS SourceObject,
 d.referenced_server_name, d.referenced_database_name,
 d.referenced_schema_name, d.referenced_entity_name, d.referenced_id,
 d.is_caller_dependent, d.is_ambiguous
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN @Objects n ON n.ObjectName = o.name
WHERE o.schema_id = SCHEMA_ID(N'dbo')
ORDER BY SourceObject, d.referenced_entity_name;

-- Alternatives to examine if a required dbo object is missing.
SELECT N'06 Alternative object names (candidates only)' AS ResultSet,
 SCHEMA_NAME(o.schema_id) AS SchemaName, o.name AS ObjectName, o.type_desc
FROM sys.objects o
WHERE o.is_ms_shipped = 0 AND o.type IN ('V', 'U', 'SN')
 AND (LOWER(o.name) LIKE N'%drop%' OR LOWER(o.name) LIKE N'%withdraw%'
   OR LOWER(o.name) LIKE N'%hold%' OR LOWER(o.name) LIKE N'%bookstore%'
   OR LOWER(o.name) LIKE N'%bibliu%'
   OR EXISTS (SELECT 1 FROM @Objects n WHERE n.ObjectName = o.name))
ORDER BY SchemaName, ObjectName;
