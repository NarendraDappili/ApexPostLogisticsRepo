-- Performance data set for ApexPostDb_Perf (see PERFORMANCE.md).
--
-- Create the database from the same migrations as every other environment, then run this script once:
--   dotnet ef database update --project src/ApexPost.Infrastructure --connection "Server=.;Database=ApexPostDb_Perf;Trusted_Connection=True;TrustServerCertificate=True"
--   sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\01-load-perf-data.sql
--
-- Volumes (one year of trading, 1 Nov 2025 to 31 Oct 2026):
--   200 branches in 10 regions, 3 counters each, 800 staff, 1,000 products, 200,000 branch stock rows,
--   10 zones / 60 rates, 200,000 counter transactions, about 528,000 lines, 100,000 shipments,
--   about 29,600 money orders and 10,500 identity verifications.
-- Distribution, chosen to look like a real network rather than a uniform grid:
--   * BR-0001 is the flagship city branch with 20% of all transactions; the other 199 branches share the rest.
--   * Transactions arrive in time order, so the identity key and OccurredAtUtc rise together, as in production.
--   * 96% of transactions are Paid, 2% Refunded and 2% Voided. (The status rule never matches a multiple of 5, so the
--     flagship's 40,000 transactions are all Paid; Status is a residual predicate in every measured plan.)
--   * About 2% of stock rows are at or below 10 units, the default low-stock threshold.
-- All values are derived from the row number (no RAND or NEWID), so every run produces the same data.
-- All names are fictional. Identity documents are stored masked, as the application stores them.

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'ApexPostDb_Perf'
    THROW 50901, 'This script loads performance data and runs only in the ApexPostDb_Perf database.', 1;
IF EXISTS (SELECT 1 FROM dbo.Branches)
    THROW 50902, 'ApexPostDb_Perf already contains data. Drop and recreate it from the migrations before loading.', 1;

-- A measurement database: no point-in-time restore is needed, and SIMPLE recovery keeps the log small during the load.
ALTER DATABASE CURRENT SET RECOVERY SIMPLE;

DECLARE @Start datetime2(3) = '2025-11-01T00:00:00';

-- Row-number source: 1 .. 1,000,000
CREATE TABLE #Numbers (n int NOT NULL PRIMARY KEY);
WITH Digits AS (SELECT d FROM (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) AS v (d))
INSERT #Numbers (n)
SELECT 1 + a.d + 10 * b.d + 100 * c.d + 1000 * e.d + 10000 * f.d + 100000 * g.d
FROM Digits AS a CROSS JOIN Digits AS b CROSS JOIN Digits AS c CROSS JOIN Digits AS e CROSS JOIN Digits AS f CROSS JOIN Digits AS g;

BEGIN TRANSACTION;

-- Organisation ---------------------------------------------------------------------------------------------------
INSERT dbo.Branches (BranchCode, Name, Region, OpeningHours, IsOpen, CreatedBy)
SELECT CONCAT('BR-', FORMAT(n, '0000')),
       CONCAT(N'Perf Branch ', FORMAT(n, '0000')),
       CHOOSE((n - 1) % 10 + 1, N'North', N'South', N'East', N'West', N'Central', N'Highlands', N'Coast', N'Valley', N'Lakes', N'Plains'),
       N'Mon-Fri 09:00-17:30',
       1,
       'perf-load'
FROM #Numbers WHERE n <= 200;

INSERT dbo.Counters (CounterCode, BranchCode, IsOpen, CreatedBy)
SELECT CONCAT('C-0', c.n), b.BranchCode, 1, 'perf-load'
FROM dbo.Branches AS b CROSS JOIN #Numbers AS c
WHERE c.n <= 3;

-- Per branch: three counter clerks (RoleId 0) and one branch manager (RoleId 1). No e-mail or password: these
-- accounts exist only so foreign keys hold; nobody signs in to the performance database.
INSERT dbo.Staff (StaffCode, Name, Email, RoleId, BranchCode, PasswordHash, IsActive, CreatedBy)
SELECT CONCAT('STF-P', RIGHT(b.BranchCode, 4), CASE WHEN r.n = 4 THEN '-M' ELSE CONCAT('-C', r.n) END),
       CONCAT(N'Perf ', CASE WHEN r.n = 4 THEN N'Manager ' ELSE N'Clerk ' END, RIGHT(b.BranchCode, 4), N'-', r.n),
       NULL,
       CASE WHEN r.n = 4 THEN 1 ELSE 0 END,
       b.BranchCode, NULL, 1, 'perf-load'
FROM dbo.Branches AS b CROSS JOIN #Numbers AS r
WHERE r.n <= 4;

-- Catalogue and stock --------------------------------------------------------------------------------------------
INSERT dbo.ProductCategories (CategoryCode, Name, CreatedBy)
VALUES ('STAMPS', N'Stamps', 'perf-load'), ('STATIONERY', N'Stationery', 'perf-load'), ('PACKAGING', N'Packaging', 'perf-load');

INSERT dbo.Products (ProductCode, Sku, Name, CategoryCode, UnitPrice, CreatedBy)
SELECT CONCAT('PRD-P', FORMAT(n, '0000')),
       CONCAT('SKU-P', FORMAT(n, '0000')),
       CONCAT(N'Perf Product ', FORMAT(n, '0000')),
       CHOOSE(n % 3 + 1, 'STAMPS', 'STATIONERY', 'PACKAGING'),
       1.00 + (n % 200) * 0.25,
       'perf-load'
FROM #Numbers WHERE n <= 1000;

-- Every branch stocks every product. About one row in 50 is at 0..10 units.
INSERT dbo.BranchStock (BranchCode, ProductCode, Quantity, CreatedBy)
SELECT CONCAT('BR-', FORMAT(b.n, '0000')),
       CONCAT('PRD-P', FORMAT(p.n, '0000')),
       CASE WHEN (b.n * 7 + p.n * 13) % 50 = 0 THEN (b.n + p.n) % 11 ELSE 20 + (b.n * 31 + p.n * 17) % 480 END,
       'perf-load'
FROM #Numbers AS b CROSS JOIN #Numbers AS p
WHERE b.n <= 200 AND p.n <= 1000;

-- Shipping -------------------------------------------------------------------------------------------------------
INSERT dbo.ShippingZones (ZoneCode, Name, CreatedBy)
SELECT CONCAT('ZONE-', FORMAT(n, '00')), CONCAT(N'Perf Zone ', FORMAT(n, '00')), 'perf-load'
FROM #Numbers WHERE n <= 10;

INSERT dbo.ShippingRates (ShipmentType, ServiceLevel, ZoneCode, Charge, MaximumWeightKg, CreatedBy)
SELECT t.ShipmentType, l.ServiceLevel, CONCAT('ZONE-', FORMAT(z.n, '00')),
       8.00 + z.n + l.LevelIndex * 3 + t.TypeIndex * 2,
       5.000 + l.LevelIndex * 5,
       'perf-load'
FROM #Numbers AS z
CROSS JOIN (VALUES ('Letter', 0), ('Parcel', 1)) AS t (ShipmentType, TypeIndex)
CROSS JOIN (VALUES ('Standard', 0), ('Express', 1), ('Registered', 2)) AS l (ServiceLevel, LevelIndex)
WHERE z.n <= 10;

-- Counter transactions -------------------------------------------------------------------------------------------
-- One row per transaction, with everything derived from its number n.
CREATE TABLE #Sales
(
    n             int          NOT NULL PRIMARY KEY,
    TxCode        varchar(48)  NOT NULL,
    BranchNumber  int          NOT NULL,
    BranchCode    varchar(48)  NOT NULL,
    OccurredAtUtc datetime2(3) NOT NULL,
    Status        varchar(10)  NOT NULL
);

INSERT #Sales (n, TxCode, BranchNumber, BranchCode, OccurredAtUtc, Status)
SELECT n, CONCAT('TX-P', FORMAT(n, '000000')), BranchNumber, CONCAT('BR-', FORMAT(BranchNumber, '0000')),
       DATEADD(SECOND, n * 157, @Start),                                  -- 200,000 x 157 s = 363 days, in time order
       CASE n % 50 WHEN 7 THEN 'Refunded' WHEN 23 THEN 'Voided' ELSE 'Paid' END
FROM #Numbers
CROSS APPLY (SELECT CASE WHEN n % 5 = 0 THEN 1                           -- the flagship branch: 20%
                         ELSE 2 + CAST((CAST(n AS bigint) * 2654435761) % 199 AS int) END AS BranchNumber) AS b
WHERE n <= 200000;

-- Lines: 1 to 3 catalogue lines per sale, a shipment line on every second sale, a money-order line on one sale in 7.
CREATE TABLE #Lines
(
    n           int           NOT NULL,
    LineNumber      int           NOT NULL,
    ProductCode varchar(48)   NULL,
    Description nvarchar(250) NOT NULL,
    Quantity    int           NOT NULL,
    UnitPrice   decimal(18, 2) NOT NULL,
    PRIMARY KEY (n, LineNumber)
);

INSERT #Lines (n, LineNumber, ProductCode, Description, Quantity, UnitPrice)
SELECT s.n, k.n, p.ProductCode, p.Name, 1 + (s.n + k.n) % 3, p.UnitPrice
FROM #Sales AS s
CROSS JOIN #Numbers AS k
INNER JOIN dbo.Products AS p ON p.ProductCode = CONCAT('PRD-P', FORMAT((s.n * 37 + k.n * 101) % 1000 + 1, '0000'))
WHERE k.n <= 1 + s.n % 3;

CREATE TABLE #Shipments
(
    n            int          NOT NULL PRIMARY KEY,
    ShipmentCode varchar(48)  NOT NULL,
    ZoneCode     varchar(48)  NOT NULL,
    ShipmentType varchar(10)  NOT NULL,
    ServiceLevel varchar(12)  NOT NULL,
    WeightKg     decimal(9, 3) NOT NULL
);

INSERT #Shipments (n, ShipmentCode, ZoneCode, ShipmentType, ServiceLevel, WeightKg)
SELECT n, CONCAT('SHP-P', FORMAT(n, '000000')),
       CONCAT('ZONE-', FORMAT((n / 2) % 10 + 1, '00')),
       CASE WHEN n % 4 = 0 THEN 'Parcel' ELSE 'Letter' END,
       CHOOSE((n / 2) % 3 + 1, 'Standard', 'Express', 'Registered'),
       0.250 + (n % 19) * 0.250                                           -- 0.25 .. 4.75 kg, inside every rate band
FROM #Sales
WHERE n % 2 = 0;

INSERT #Lines (n, LineNumber, ProductCode, Description, Quantity, UnitPrice)
SELECT h.n, 10, NULL, CONCAT(h.ServiceLevel, ' ', LOWER(h.ShipmentType), ' ', h.ShipmentCode, ' to ', h.ZoneCode), 1, r.Charge
FROM #Shipments AS h
INNER JOIN dbo.ShippingRates AS r
        ON r.ZoneCode = h.ZoneCode AND r.ShipmentType = h.ShipmentType AND r.ServiceLevel = h.ServiceLevel;

CREATE TABLE #MoneyOrders
(
    n              int           NOT NULL PRIMARY KEY,
    MoneyOrderCode varchar(48)   NOT NULL,
    Amount         decimal(18, 2) NOT NULL,
    Fee            decimal(18, 2) NOT NULL
);

-- One in three money orders is high value (over the 1,000.00 threshold); the fee is 2.50 plus 1%.
INSERT #MoneyOrders (n, MoneyOrderCode, Amount, Fee)
SELECT n, CONCAT('MO-P', FORMAT(n, '000000')), a.Amount, 2.50 + ROUND(a.Amount * 0.01, 2)
FROM #Sales
CROSS APPLY (SELECT CASE WHEN n % 3 = 0 THEN 1500.00 ELSE 300.00 END AS Amount) AS a
WHERE n % 7 = 3;

INSERT #Lines (n, LineNumber, ProductCode, Description, Quantity, UnitPrice)
SELECT n, 20, NULL, CONCAT('Money order ', MoneyOrderCode), 1, Amount + Fee
FROM #MoneyOrders;

-- Headers, with the subtotal of their lines. Tax is 0, the default ApexPost:Transactions:TaxRate.
INSERT dbo.CounterTransactions
    (TransactionCode, BranchCode, CounterCode, ClerkStaffCode, Subtotal, Tax, OccurredAtUtc, Status, StatusReason,
     AuthorizedByStaffCode, CreatedAtUtc, CreatedBy)
SELECT s.TxCode, s.BranchCode, CONCAT('C-0', s.n % 3 + 1),
       CONCAT('STF-P', FORMAT(s.BranchNumber, '0000'), '-C', s.n % 3 + 1),
       t.Subtotal, 0.00, s.OccurredAtUtc, s.Status,
       CASE s.Status WHEN 'Refunded' THEN N'Customer returned unused items.' WHEN 'Voided' THEN N'Basket keyed at the wrong counter.' END,
       CASE WHEN s.Status IN ('Refunded', 'Voided') THEN CONCAT('STF-P', FORMAT(s.BranchNumber, '0000'), '-M') END,
       s.OccurredAtUtc, 'perf-load'
FROM #Sales AS s
CROSS APPLY (SELECT SUM(l.UnitPrice * l.Quantity) AS Subtotal FROM #Lines AS l WHERE l.n = s.n) AS t
ORDER BY s.n;                                                              -- identity values follow time order

INSERT dbo.TransactionLines (CounterTransactionId, ProductCode, Description, Quantity, UnitPrice, CreatedAtUtc, CreatedBy)
SELECT t.CounterTransactionId, l.ProductCode, l.Description, l.Quantity, l.UnitPrice, s.OccurredAtUtc, 'perf-load'
FROM #Lines AS l
INNER JOIN #Sales AS s ON s.n = l.n
INNER JOIN dbo.CounterTransactions AS t ON t.TransactionCode = s.TxCode
ORDER BY l.n, l.LineNumber;

INSERT dbo.Payments (CounterTransactionId, PaymentMethod, Amount, CreatedAtUtc, CreatedBy)
SELECT t.CounterTransactionId, CHOOSE(s.n % 3 + 1, 'Cash', 'Card', 'DigitalWallet'), t.Subtotal + t.Tax, s.OccurredAtUtc, 'perf-load'
FROM #Sales AS s
INNER JOIN dbo.CounterTransactions AS t ON t.TransactionCode = s.TxCode;

INSERT dbo.Shipments
    (ShipmentCode, OriginBranchCode, ZoneCode, WeightKg, LengthCm, WidthCm, HeightCm, ShipmentType, ServiceLevel,
     TransactionCode, CreatedAtUtc, CreatedBy)
SELECT h.ShipmentCode, s.BranchCode, h.ZoneCode, h.WeightKg, 30.00, 20.00, 10.00, h.ShipmentType, h.ServiceLevel,
       s.TxCode, s.OccurredAtUtc, 'perf-load'
FROM #Shipments AS h
INNER JOIN #Sales AS s ON s.n = h.n
ORDER BY h.n;

-- Money orders: the ones sold above, plus one refused order per 200 transactions (a failed identity check, no sale).
INSERT dbo.MoneyOrders (MoneyOrderCode, BranchCode, Amount, Fee, Sender, Recipient, TransactionContextCode, CreatedAtUtc, CreatedBy)
SELECT m.MoneyOrderCode, s.BranchCode, m.Amount, m.Fee,
       CONCAT(N'Synthetic Sender ', m.n), CONCAT(N'Synthetic Recipient ', m.n), s.TxCode, s.OccurredAtUtc, 'perf-load'
FROM #MoneyOrders AS m
INNER JOIN #Sales AS s ON s.n = m.n
UNION ALL
SELECT CONCAT('MO-PR', FORMAT(s.n, '000000')), s.BranchCode, 2000.00, 22.50,
       CONCAT(N'Synthetic Sender R', s.n), CONCAT(N'Synthetic Recipient R', s.n), CONCAT('TX-PR', FORMAT(s.n, '000000')),
       s.OccurredAtUtc, 'perf-load'
FROM #Sales AS s
WHERE s.n % 200 = 100;

-- High-value orders that were sold carry a successful check; refused orders carry a failed one.
INSERT dbo.IdentityVerifications (MoneyOrderId, DocumentType, MaskedDocumentReference, Outcome, CreatedAtUtc, CreatedBy)
SELECT mo.MoneyOrderId,
       CASE WHEN mo.MoneyOrderCode LIKE 'MO-PR%' THEN 'Driving licence' ELSE 'Passport' END,
       CASE WHEN mo.MoneyOrderCode LIKE 'MO-PR%' THEN '****0000' ELSE '****4821' END,
       CASE WHEN mo.MoneyOrderCode LIKE 'MO-PR%' THEN 'Failed' ELSE 'Verified' END,
       mo.CreatedAtUtc, 'perf-load'
FROM dbo.MoneyOrders AS mo
WHERE mo.Amount > 1000.00
ORDER BY mo.MoneyOrderId;

COMMIT TRANSACTION;

-- Fresh, full-scan statistics, so the baseline plans are not distorted by sampling.
UPDATE STATISTICS dbo.Branches WITH FULLSCAN;
UPDATE STATISTICS dbo.Counters WITH FULLSCAN;
UPDATE STATISTICS dbo.Staff WITH FULLSCAN;
UPDATE STATISTICS dbo.Products WITH FULLSCAN;
UPDATE STATISTICS dbo.BranchStock WITH FULLSCAN;
UPDATE STATISTICS dbo.ShippingRates WITH FULLSCAN;
UPDATE STATISTICS dbo.CounterTransactions WITH FULLSCAN;
UPDATE STATISTICS dbo.TransactionLines WITH FULLSCAN;
UPDATE STATISTICS dbo.Payments WITH FULLSCAN;
UPDATE STATISTICS dbo.Shipments WITH FULLSCAN;
UPDATE STATISTICS dbo.MoneyOrders WITH FULLSCAN;
UPDATE STATISTICS dbo.IdentityVerifications WITH FULLSCAN;

SELECT t.name AS TableName, SUM(p.rows) AS Rows
FROM sys.tables AS t
INNER JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name <> '__EFMigrationsHistory'
GROUP BY t.name
ORDER BY t.name;
