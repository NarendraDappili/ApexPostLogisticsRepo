-- 21  Blocking demo, SESSION 2 (reader, then a second writer). Open this file in a second SSMS query window.
-- Run each step on its own while session 1 still holds its transaction open.
--
-- The session uses the default isolation level, READ COMMITTED. What READ COMMITTED does for readers depends on
-- the database option READ_COMMITTED_SNAPSHOT (RCSI):
SELECT name, is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();
-- A database created by EF Core migrations has RCSI ON (EF Core issues ALTER DATABASE ... SET
-- READ_COMMITTED_SNAPSHOT ON when it creates the database). The SQL Server default for CREATE DATABASE is OFF.

-- STEP A: plain read at READ COMMITTED.
--   RCSI ON:  returns at once with the last COMMITTED quantity (read from the row version store). No wait.
--   RCSI OFF: waits for session 1 exactly like STEP B.
SELECT Quantity AS LastCommittedQuantity
FROM dbo.BranchStock
WHERE BranchCode = 'BR-NORTH' AND ProductCode = 'PRD-001';

-- STEP B: READ COMMITTED with shared locks (what every READ COMMITTED read does when RCSI is OFF).
-- The reader needs an S lock on the row; session 1 holds an X lock on it, so this query waits (LCK_M_S)
-- until session 1 commits or rolls back. Watch it with 22_blocking_monitor.sql.
SELECT Quantity AS LockedReadQuantity
FROM dbo.BranchStock WITH (READCOMMITTEDLOCK)
WHERE BranchCode = 'BR-NORTH' AND ProductCode = 'PRD-001';

-- STEP C: a second writer of the same row. Writers always need an exclusive lock, so this waits (LCK_M_U)
-- whatever the RCSI setting. Row versioning removes reader/writer blocking, never writer/writer blocking.
BEGIN TRANSACTION;
UPDATE dbo.BranchStock
SET Quantity = Quantity - 1, ModifiedAtUtc = SYSUTCDATETIME()
WHERE BranchCode = 'BR-NORTH' AND ProductCode = 'PRD-001';
ROLLBACK TRANSACTION;
