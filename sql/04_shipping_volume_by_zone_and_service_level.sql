-- 04  Shipping volume by destination zone and service level
--
-- Every zone x service-level combination is listed, including those with no shipments (shown as 0).
-- That grid needs the CROSS JOIN with the three service levels plus a LEFT JOIN to the shipments.
--
-- A shipment's date is the date of the counter sale it was accepted in. Shipments.CreatedAtUtc is when the row was
-- written, which for imported or seeded rows is not the acceptance date. Shipments in a voided basket never left the
-- counter and are excluded. Shipments in a later refunded sale were still accepted, so they count as volume.

DECLARE @FromDate date = '2026-09-01';   -- inclusive
DECLARE @ToDate   date = '2026-10-31';   -- inclusive

DECLARE @FromUtc datetime2(3) = CAST(@FromDate AS datetime2(3));
DECLARE @ToUtc   datetime2(3) = DATEADD(DAY, 1, CAST(@ToDate AS datetime2(3)));

WITH Accepted AS
(
    SELECT s.ZoneCode, s.ServiceLevel, s.ShipmentType, s.WeightKg
    FROM dbo.Shipments AS s
    INNER JOIN dbo.CounterTransactions AS t ON t.TransactionCode = s.TransactionCode
    WHERE t.Status <> 'Voided'
      AND t.OccurredAtUtc >= @FromUtc
      AND t.OccurredAtUtc <  @ToUtc
)
SELECT
    z.ZoneCode,
    z.Name                                                   AS ZoneName,
    levels.ServiceLevel,
    COUNT(a.ZoneCode)                                        AS Shipments,
    SUM(CASE WHEN a.ShipmentType = 'Letter' THEN 1 ELSE 0 END) AS Letters,
    SUM(CASE WHEN a.ShipmentType = 'Parcel' THEN 1 ELSE 0 END) AS Parcels,
    CAST(ISNULL(SUM(a.WeightKg), 0) AS decimal(12, 3))       AS TotalWeightKg
FROM dbo.ShippingZones AS z
CROSS JOIN (VALUES ('Standard', 1), ('Express', 2), ('Registered', 3)) AS levels (ServiceLevel, SortOrder)
LEFT JOIN Accepted AS a
       ON a.ZoneCode = z.ZoneCode
      AND a.ServiceLevel = levels.ServiceLevel
GROUP BY z.ZoneCode, z.Name, levels.ServiceLevel, levels.SortOrder
ORDER BY z.ZoneCode, levels.SortOrder;
