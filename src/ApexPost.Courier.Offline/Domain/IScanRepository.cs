namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Stores shipment scans on the device. The methods are synchronous because the store is an embedded database on
/// local disk: there is no network wait to hide.
/// </summary>
public interface IScanRepository
{
    /// <summary>Saves a new scan. Throws <see cref="InvalidOperationException"/> if the ID is already used.</summary>
    void Create(ShipmentScan scan);

    /// <summary>Key-value lookup by document ID; <see langword="null"/> when there is no such scan.</summary>
    ShipmentScan? GetById(ScanId id);

    /// <summary>Saves changes to an existing scan. Throws <see cref="ScanNotFoundException"/> if it was deleted.</summary>
    void Update(ShipmentScan scan);

    /// <summary>Deletes a scan; returns <see langword="false"/> when there was nothing to delete.</summary>
    bool Delete(ScanId id);

    /// <summary>Every scan on the device, oldest first.</summary>
    IReadOnlyList<ShipmentScan> ListAll();

    /// <summary>The scan history of one parcel, oldest first.</summary>
    IReadOnlyList<ShipmentScan> FindByTrackingNumber(string trackingNumber);

    /// <summary>Scans saved on the device that the outbox has not uploaded yet, oldest first.</summary>
    IReadOnlyList<ShipmentScan> ListPending();

    /// <summary>How many scans of each type one courier has recorded; every type is present, zero if unused.</summary>
    IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId);
}
