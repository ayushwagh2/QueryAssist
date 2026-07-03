SELECT
    p.name AS [name],
    m.definition AS [text]
FROM sys.procedures p
JOIN sys.sql_modules m
    ON p.object_id = m.object_id
ORDER BY p.name
FOR JSON PATH, ROOT('storedProcedures');

SELECT COUNT(*) AS StoredProcedureCount
FROM sys.procedures;

SELECT
    t.TABLE_SCHEMA AS [schema],
    t.TABLE_NAME AS [name],
    (
        SELECT
            c.COLUMN_NAME AS [name],
            c.DATA_TYPE AS [type],
            c.IS_NULLABLE AS [nullable]
        FROM INFORMATION_SCHEMA.COLUMNS c
        WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA
          AND c.TABLE_NAME = t.TABLE_NAME
        ORDER BY c.ORDINAL_POSITION
        FOR JSON PATH
    ) AS columns
FROM INFORMATION_SCHEMA.TABLES t
WHERE t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
FOR JSON PATH, ROOT('tables');



SELECT
    s.name AS [schema],
    o.name AS [name],
    CASE o.type
        WHEN 'FN' THEN 'Scalar Function'
        WHEN 'IF' THEN 'Inline Table-Valued Function'
        WHEN 'TF' THEN 'Multi-Statement Table-Valued Function'
    END AS [type],

    (
        SELECT
            p.name AS [name],
            TYPE_NAME(p.user_type_id) AS [type],
            p.max_length AS [maxLength],
            p.precision AS [precision],
            p.scale AS [scale],
            p.is_output AS [isOutput]
        FROM sys.parameters p
        WHERE p.object_id = o.object_id
        ORDER BY p.parameter_id
        FOR JSON PATH
    ) AS [parameters],

    m.definition AS [definition]

FROM sys.objects o
INNER JOIN sys.schemas s
    ON o.schema_id = s.schema_id
INNER JOIN sys.sql_modules m
    ON o.object_id = m.object_id
WHERE o.type IN ('FN', 'IF', 'TF')
ORDER BY s.name, o.name
FOR JSON PATH, ROOT('functions');