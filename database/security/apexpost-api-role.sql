-- Least-privilege database role for ApexPost.Api, the tracking API (Week 4).
--
-- Run as a database owner after the migrations have been applied, then make the API's database user a member:
--   ALTER ROLE apexpost_api ADD MEMBER [<api user>];
-- The API gets its own login and role, separate from the web application's apexpost_app (apexpost-app-role.sql): a
-- leaked API credential cannot read staff, sales or money orders, and the web application cannot delete shipments.
--
-- What the role allows, and why:
--   * TrackedShipments: DELETE as well, because DELETE /api/shipments/{trackingNumber} removes a shipment that is
--     still Created. The service enforces that rule; the database only allows the statement.
--   * TrackingScans: SELECT and INSERT only. An accepted scan is history and never changes.
--   * DataChanges: SELECT and INSERT, because every API change records its history in the same transaction.
--     SELECT is needed for the OUTPUT clause EF Core uses to read back the new row's identity.
--   * Nothing else: no Week 3 table, no procedure, no DDL, no schema-wide grant.
-- The script is re-runnable: GRANT is idempotent and the role is created only once.

IF DATABASE_PRINCIPAL_ID(N'apexpost_api') IS NULL
    CREATE ROLE apexpost_api AUTHORIZATION dbo;

-- Tracking
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TrackedShipments TO apexpost_api;
GRANT SELECT, INSERT ON dbo.TrackingScans TO apexpost_api;

-- Audit trail: append and read back only
GRANT SELECT, INSERT ON dbo.DataChanges TO apexpost_api;
