-- Single vs split query for a branch's sales with their lines (ADR 0020, PERFORMANCE.md "Single vs split query").
--
-- The three SELECTs are the exact text EF Core sends (captured by GeneratedSqlTests): first the default single query,
-- then the two statements of AsSplitQuery(). Read-only; run it against any migrated database:
--   sqlcmd -S . -E -d ApexPostDb -I -i database\perf\05-single-vs-split-query.sql
-- Compare the "rows affected" lines and the logical reads per statement.

SET NOCOUNT OFF;
DECLARE @data_Branch varchar(48) = 'BR-NORTH';
SET STATISTICS IO ON;

PRINT '--- single query';
SELECT [c].[CounterTransactionId], [c].[AuthorizedByStaffCode], [c].[BranchCode], [c].[ClerkStaffCode], [c].[CounterCode], [c].[CreatedAtUtc], [c].[CreatedBy], [c].[TransactionCode], [c].[ModifiedAtUtc], [c].[OccurredAtUtc], [c].[RowVersion], [c].[Status], [c].[StatusReason], [c].[Subtotal], [c].[Tax], [p].[CounterTransactionId], [t].[TransactionLineId], [t].[CounterTransactionId], [t].[CreatedAtUtc], [t].[CreatedBy], [t].[Description], [t].[ModifiedAtUtc], [t].[ProductCode], [t].[Quantity], [t].[UnitPrice], [p].[Amount], [p].[CreatedAtUtc], [p].[CreatedBy], [p].[PaymentMethod], [p].[ModifiedAtUtc]
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
LEFT JOIN [TransactionLines] AS [t] ON [c].[CounterTransactionId] = [t].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId];

PRINT '--- split query, statement 1 (sales and payments)';
SELECT [c].[CounterTransactionId], [c].[AuthorizedByStaffCode], [c].[BranchCode], [c].[ClerkStaffCode], [c].[CounterCode], [c].[CreatedAtUtc], [c].[CreatedBy], [c].[TransactionCode], [c].[ModifiedAtUtc], [c].[OccurredAtUtc], [c].[RowVersion], [c].[Status], [c].[StatusReason], [c].[Subtotal], [c].[Tax], [p].[CounterTransactionId], [p].[Amount], [p].[CreatedAtUtc], [p].[CreatedBy], [p].[PaymentMethod], [p].[ModifiedAtUtc]
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId];

PRINT '--- split query, statement 2 (lines)';
SELECT [t0].[TransactionLineId], [t0].[CounterTransactionId], [t0].[CreatedAtUtc], [t0].[CreatedBy], [t0].[Description], [t0].[ModifiedAtUtc], [t0].[ProductCode], [t0].[Quantity], [t0].[UnitPrice], [c].[CounterTransactionId], [p].[CounterTransactionId]
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
INNER JOIN [TransactionLines] AS [t0] ON [c].[CounterTransactionId] = [t0].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId];

SET STATISTICS IO OFF;
