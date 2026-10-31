-- Least-privilege database role for the ApexPost web application.
--
-- Run as a database owner after the migrations have been applied, then make the application's database user a
-- member:  ALTER ROLE apexpost_app ADD MEMBER [<application user>];
-- The application login is NOT a db_owner, db_ddladmin, db_datawriter or db_datareader member. Migrations and the
-- Development demo seed run under a separate deployment/developer login.
--
-- What the role allows, and why:
--   * SELECT/INSERT/UPDATE only on the tables the use cases write; SELECT only on reference data.
--   * No DELETE anywhere: branches, staff and products are closed or deactivated, never deleted.
--   * AuditEntries: SELECT and INSERT only, so the audit trail is append-only for the application.
--   * StockAdjustments: no table permission at all. History rows are written only by usp_AdjustStock, which reaches
--     the table through ownership chaining (both are owned by dbo).
--   * EXECUTE on the three procedures; no other procedures, no DDL, no schema-wide grants.
-- The script is re-runnable: GRANT is idempotent and the role is created only once.

IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NULL
    CREATE ROLE apexpost_app AUTHORIZATION dbo;

-- Reference data
GRANT SELECT ON dbo.Roles TO apexpost_app;
GRANT SELECT ON dbo.ProductCategories TO apexpost_app;
GRANT SELECT ON dbo.ShippingZones TO apexpost_app;
GRANT SELECT ON dbo.ShippingRates TO apexpost_app;

-- Organisation (staff and branch administration, sign-in)
GRANT SELECT, INSERT, UPDATE ON dbo.Branches TO apexpost_app;
GRANT SELECT, INSERT ON dbo.Counters TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON dbo.Staff TO apexpost_app;

-- Catalogue and stock (price revision updates Products; sales and first deliveries write BranchStock)
GRANT SELECT, UPDATE ON dbo.Products TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON dbo.BranchStock TO apexpost_app;

-- Counter work
GRANT SELECT, INSERT ON dbo.Shipments TO apexpost_app;
GRANT SELECT, INSERT ON dbo.MoneyOrders TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON dbo.IdentityVerifications TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON dbo.CounterTransactions TO apexpost_app;
GRANT SELECT, INSERT ON dbo.TransactionLines TO apexpost_app;
GRANT SELECT, INSERT ON dbo.Payments TO apexpost_app;

-- Audit trail: append and read only
GRANT SELECT, INSERT ON dbo.AuditEntries TO apexpost_app;

-- Stored procedures
GRANT EXECUTE ON dbo.usp_DailyBranchSales TO apexpost_app;
GRANT EXECUTE ON dbo.usp_LowStockReport TO apexpost_app;
GRANT EXECUTE ON dbo.usp_AdjustStock TO apexpost_app;
