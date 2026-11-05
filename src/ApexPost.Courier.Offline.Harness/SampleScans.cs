using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Harness;

/// <summary>Fictional scans for a first run: invented tracking numbers, places and names.</summary>
internal static class SampleScans
{
    public static IReadOnlyList<ShipmentScan> Create(string courierId, DateTimeOffset now) =>
    [
        ShipmentScan.Record("APX-24000001", ScanType.Pickup, courierId, "Riverside Depot", now.AddHours(-4), "Collected from front desk"),
        ShipmentScan.Record("APX-24000001", ScanType.InTransit, courierId, "North Hub, Bay 3", now.AddHours(-2)),
        ShipmentScan.Record("APX-24000002", ScanType.Pickup, courierId, "Riverside Depot", now.AddHours(-3)),
        ShipmentScan.Record("APX-24000003", ScanType.Delivered, courierId, "14 Elm Street, Lakeview", now.AddHours(-1), "Signed by J. Fictional"),
        ShipmentScan.Record("APX-24000004", ScanType.Failed, courierId, "7 Harbour Road, Lakeview", now.AddMinutes(-30), "Nobody home, card left"),
    ];
}
