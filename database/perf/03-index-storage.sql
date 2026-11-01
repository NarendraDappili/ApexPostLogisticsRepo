-- Storage per index on the tables the workload indexes touch (PERFORMANCE.md, "Storage cost").
-- used_page_count includes the non-leaf levels; 1 page = 8 KB.

SET NOCOUNT ON;

SELECT
    OBJECT_NAME(i.object_id)                         AS TableName,
    i.name                                           AS IndexName,
    i.type_desc                                      AS IndexType,
    SUM(ps.row_count)                                AS Rows,
    SUM(ps.used_page_count)                          AS UsedPages,
    CAST(SUM(ps.used_page_count) * 8 / 1024.0 AS decimal(9, 2)) AS UsedMB
FROM sys.indexes AS i
INNER JOIN sys.dm_db_partition_stats AS ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE OBJECT_NAME(i.object_id) IN (N'BranchStock', N'CounterTransactions', N'TransactionLines', N'Shipments', N'IdentityVerifications')
GROUP BY i.object_id, i.name, i.type_desc, i.index_id
ORDER BY TableName, i.index_id;
