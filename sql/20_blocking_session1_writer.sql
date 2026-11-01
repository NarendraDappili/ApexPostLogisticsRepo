-- 20  Blocking demo, SESSION 1 (writer). Open this file in its own SSMS query window.
--
-- Run against a copy of the database, not production: the update is rolled back at the end, but while it is open
-- it blocks other writers of this stock row (and readers that use locking reads).
-- Order: run STEP 1 here, then the steps in 21_blocking_session2_reader.sql, while 22_blocking_monitor.sql watches.
-- Finish with STEP 2 here.

-- STEP 1: start a transaction that changes one stock row and keep it open.
BEGIN TRANSACTION;

UPDATE dbo.BranchStock
SET Quantity = Quantity - 1, ModifiedAtUtc = SYSUTCDATETIME()
WHERE BranchCode = 'BR-NORTH' AND ProductCode = 'PRD-001';

SELECT @@SPID AS WriterSessionId, @@TRANCOUNT AS OpenTransactions,
       (SELECT Quantity FROM dbo.BranchStock WHERE BranchCode = 'BR-NORTH' AND ProductCode = 'PRD-001') AS UncommittedQuantity;

-- STEP 2 (run when the observations are done): end the transaction. Rolling back leaves the demo data unchanged.
-- ROLLBACK TRANSACTION;
