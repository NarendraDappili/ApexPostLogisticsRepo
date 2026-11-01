-- Write overhead of the workload indexes (PERFORMANCE.md, "Write overhead").
--
-- Replays the application's typical writes inside one transaction and ROLLS IT BACK, so the data set is identical
-- before and after the index migration and the two runs are comparable:
--   * 2,000 stock movements, one UPDATE per row by primary key, as EF Core sends them (Quantity changes, so every
--     index with Quantity in its key must move the row);
--   * 1,000 sales: header, 2 lines each and a payment (EF Core batches these inserts);
--   * 500 shipments;
--   * 200 money orders with an identity verification.
-- It reports the CPU, logical reads and writes of this request, the transaction log it generated, and the leaf-level
-- row operations and new leaf pages (page splits) per index (sys.dm_db_index_operational_stats), which show exactly
-- which indexes had to be maintained.
-- Run with: sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\04-write-overhead.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'ApexPostDb_Perf'
    THROW 50903, 'This script runs only in the ApexPostDb_Perf database.', 1;

DECLARE @Tables TABLE (ObjectId int PRIMARY KEY);
INSERT @Tables (ObjectId)
SELECT OBJECT_ID(name) FROM (VALUES (N'dbo.BranchStock'), (N'dbo.CounterTransactions'), (N'dbo.TransactionLines'),
                                    (N'dbo.Payments'), (N'dbo.Shipments'), (N'dbo.MoneyOrders'), (N'dbo.IdentityVerifications')) AS t (name);

DECLARE @Before TABLE (ObjectId int, IndexId int, Inserts bigint, Updates bigint, Deletes bigint, Allocations bigint, PRIMARY KEY (ObjectId, IndexId));
INSERT @Before
SELECT os.object_id, os.index_id, os.leaf_insert_count, os.leaf_update_count, os.leaf_delete_count + os.leaf_ghost_count, os.leaf_allocation_count
FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL) AS os
INNER JOIN @Tables AS t ON t.ObjectId = os.object_id;

DECLARE @Cpu0 int, @Reads0 bigint, @Writes0 bigint;
SELECT @Cpu0 = cpu_time, @Reads0 = logical_reads, @Writes0 = writes FROM sys.dm_exec_requests WHERE session_id = @@SPID;

DECLARE @Now datetime2(3) = '2026-10-31T12:00:00';

BEGIN TRANSACTION;

-- 1. Stock movements, one row at a time (spread over the whole table).
DECLARE @i int = 0;
WHILE @i < 2000
BEGIN
    UPDATE dbo.BranchStock
    SET Quantity = Quantity + 1, ModifiedAtUtc = @Now
    WHERE BranchStockId = 1 + (@i * 97) % 200000;
    SET @i += 1;
END;

-- 2. 1,000 sales at the flagship branch, each with two lines and a payment.
INSERT dbo.CounterTransactions (TransactionCode, BranchCode, CounterCode, ClerkStaffCode, Subtotal, Tax, OccurredAtUtc, Status, CreatedAtUtc, CreatedBy)
SELECT CONCAT('TX-W', n), 'BR-0001', 'C-01', 'STF-P0001-C1', 12.50, 0.00, DATEADD(SECOND, n, @Now), 'Paid', @Now, 'perf-write'
FROM (SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns) AS x;

INSERT dbo.TransactionLines (CounterTransactionId, ProductCode, Description, Quantity, UnitPrice, CreatedAtUtc, CreatedBy)
SELECT t.CounterTransactionId, l.ProductCode, N'Perf write line', 1, 6.25, @Now, 'perf-write'
FROM dbo.CounterTransactions AS t
CROSS JOIN (VALUES ('PRD-P0001'), ('PRD-P0002')) AS l (ProductCode)
WHERE t.TransactionCode LIKE 'TX-W%';

INSERT dbo.Payments (CounterTransactionId, PaymentMethod, Amount, CreatedAtUtc, CreatedBy)
SELECT t.CounterTransactionId, 'Card', 12.50, @Now, 'perf-write'
FROM dbo.CounterTransactions AS t
WHERE t.TransactionCode LIKE 'TX-W%';

-- 3. 500 shipments.
INSERT dbo.Shipments (ShipmentCode, OriginBranchCode, ZoneCode, WeightKg, LengthCm, WidthCm, HeightCm, ShipmentType, ServiceLevel, TransactionCode, CreatedAtUtc, CreatedBy)
SELECT CONCAT('SHP-W', n), 'BR-0001', CONCAT('ZONE-0', n % 9 + 1), 1.000, 30, 20, 10, 'Parcel', 'Express', CONCAT('TX-W', n), @Now, 'perf-write'
FROM (SELECT TOP (500) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns) AS x;

-- 4. 200 high-value money orders, each with a verification.
INSERT dbo.MoneyOrders (MoneyOrderCode, BranchCode, Amount, Fee, Sender, Recipient, TransactionContextCode, CreatedAtUtc, CreatedBy)
SELECT CONCAT('MO-W', n), 'BR-0001', 1500.00, 17.50, N'Synthetic Sender', N'Synthetic Recipient', CONCAT('TX-W', n), @Now, 'perf-write'
FROM (SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns) AS x;

INSERT dbo.IdentityVerifications (MoneyOrderId, DocumentType, MaskedDocumentReference, Outcome, CreatedAtUtc, CreatedBy)
SELECT MoneyOrderId, 'Passport', '****4821', 'Verified', @Now, 'perf-write'
FROM dbo.MoneyOrders
WHERE MoneyOrderCode LIKE 'MO-W%';

DECLARE @LogBytes bigint, @LogRecords bigint;
SELECT @LogBytes = database_transaction_log_bytes_used, @LogRecords = database_transaction_log_record_count
FROM sys.dm_tran_database_transactions
WHERE transaction_id = CURRENT_TRANSACTION_ID() AND database_id = DB_ID();

DECLARE @Cpu1 int, @Reads1 bigint, @Writes1 bigint;
SELECT @Cpu1 = cpu_time, @Reads1 = logical_reads, @Writes1 = writes FROM sys.dm_exec_requests WHERE session_id = @@SPID;

-- Leaf-level row operations per index while the transaction is still open (rollback would add its own undo work).
SELECT
    OBJECT_NAME(os.object_id)                              AS TableName,
    i.name                                                 AS IndexName,
    os.leaf_insert_count - b.Inserts                       AS LeafInserts,
    os.leaf_update_count - b.Updates                       AS LeafUpdates,
    os.leaf_delete_count + os.leaf_ghost_count - b.Deletes AS LeafDeletes,
    os.leaf_allocation_count - b.Allocations               AS LeafPageAllocations
FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL) AS os
INNER JOIN @Before AS b ON b.ObjectId = os.object_id AND b.IndexId = os.index_id
INNER JOIN sys.indexes AS i ON i.object_id = os.object_id AND i.index_id = os.index_id
WHERE os.leaf_insert_count - b.Inserts + os.leaf_update_count - b.Updates + os.leaf_delete_count + os.leaf_ghost_count - b.Deletes > 0
ORDER BY TableName, os.index_id;

ROLLBACK TRANSACTION;

SELECT
    @Cpu1 - @Cpu0       AS CpuMs,
    @Reads1 - @Reads0   AS LogicalReads,
    @Writes1 - @Writes0 AS PhysicalWrites,
    @LogRecords         AS LogRecords,
    @LogBytes           AS LogBytes;
