-- 22  Blocking demo, MONITOR. Open this file in a third SSMS query window and run it while session 2 is waiting.

-- Who is waiting, on whom, for what, and at which isolation level (2 = READ COMMITTED)
SELECT
    r.session_id,
    r.blocking_session_id,
    r.status,
    r.command,
    r.wait_type,
    r.wait_time        AS wait_time_ms,
    r.wait_resource,
    r.transaction_isolation_level,
    SUBSTRING(t.text, r.statement_start_offset / 2 + 1,
              (CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text) ELSE r.statement_end_offset END
               - r.statement_start_offset) / 2 + 1) AS waiting_statement
FROM sys.dm_exec_requests AS r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
WHERE r.blocking_session_id <> 0;

-- The locks held (GRANT) and requested (WAIT) by the blocked sessions and their blockers in this database
SELECT
    l.request_session_id AS session_id,
    l.resource_type,
    CASE l.resource_type WHEN 'OBJECT' THEN OBJECT_NAME(l.resource_associated_entity_id)
                         ELSE OBJECT_NAME(p.object_id) END AS object_name,
    i.name               AS index_name,
    l.resource_description,
    l.request_mode,
    l.request_status
FROM sys.dm_tran_locks AS l
LEFT JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id AND l.resource_type IN ('KEY', 'PAGE', 'RID', 'HOBT')
LEFT JOIN sys.indexes    AS i ON i.object_id = p.object_id AND i.index_id = p.index_id
WHERE l.resource_database_id = DB_ID()
  AND l.resource_type <> 'DATABASE'
  AND l.request_session_id IN (SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id <> 0
                               UNION
                               SELECT blocking_session_id FROM sys.dm_exec_requests WHERE blocking_session_id <> 0)
ORDER BY l.request_session_id, l.resource_type, l.request_status;
