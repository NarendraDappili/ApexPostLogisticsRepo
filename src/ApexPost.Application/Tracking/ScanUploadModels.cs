using ApexPost.Application.Abstractions;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Tracking;

/// <summary>One scan as the courier's device recorded it offline (Part A), with the ID the device gave it.</summary>
public sealed record UploadedScan(string ScanId, TrackingScanType ScanType, string Location, string CourierId, DateTimeOffset ScannedAtUtc, string? Notes);

/// <summary>
/// What became of an upload. Every scan is counted once: accepted (stored now), a duplicate (stored by an earlier
/// upload, so skipped) or rejected (with the reason; nothing stored). Accepted and duplicate scans are both safely
/// on the server, so the device marks both as synced and only the rejected ones as failed.
/// </summary>
public sealed record ScanUploadResult(int Accepted, int DuplicatesSkipped, IReadOnlyList<ScanRejection> Rejected, TrackingStatus ShipmentStatus);

public sealed record ScanRejection(string ScanId, string Reason);

/// <summary>
/// Another request saved scans or a status change for the same shipment while this upload was being decided, so
/// it was not saved. Sending it again is safe: anything already stored comes back as a duplicate.
/// </summary>
public sealed class ScanUploadConflictException(string trackingNumber, Exception innerException)
    : ConflictException($"Shipment {trackingNumber} changed while these scans were being recorded, so nothing was saved. Send the upload again; scans already stored will be reported as duplicates.", innerException);
