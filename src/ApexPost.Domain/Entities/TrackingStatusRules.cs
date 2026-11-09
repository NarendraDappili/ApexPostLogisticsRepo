using ApexPost.Domain.Enums;

namespace ApexPost.Domain.Entities;

/// <summary>
/// The tracking status chain, Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed, and the status
/// each scan type stands for. Delivered and Failed are final.
/// </summary>
public static class TrackingStatusRules
{
    private static readonly Dictionary<TrackingStatus, TrackingStatus[]> Next = new()
    {
        [TrackingStatus.Created] = [TrackingStatus.PickedUp],
        [TrackingStatus.PickedUp] = [TrackingStatus.InTransit],
        [TrackingStatus.InTransit] = [TrackingStatus.OutForDelivery],
        [TrackingStatus.OutForDelivery] = [TrackingStatus.Delivered, TrackingStatus.Failed],
        [TrackingStatus.Delivered] = [],
        [TrackingStatus.Failed] = [],
    };

    /// <summary>The statuses a shipment may move to next; empty once it is final.</summary>
    public static IReadOnlyList<TrackingStatus> AllowedAfter(TrackingStatus status) =>
        Next.TryGetValue(status, out var next) ? next : throw new ArgumentOutOfRangeException(nameof(status));

    public static bool IsAllowed(TrackingStatus from, TrackingStatus to) => AllowedAfter(from).Contains(to);

    public static bool IsFinal(TrackingStatus status) => AllowedAfter(status).Count == 0;

    /// <summary>The status a scan of this type puts a shipment in.</summary>
    public static TrackingStatus StatusFor(TrackingScanType scanType) => scanType switch
    {
        TrackingScanType.Pickup => TrackingStatus.PickedUp,
        TrackingScanType.InTransit => TrackingStatus.InTransit,
        TrackingScanType.Delivered => TrackingStatus.Delivered,
        TrackingScanType.Failed => TrackingStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(scanType)),
    };

    /// <summary>How far along the chain a status is; Delivered and Failed are both the last stage.</summary>
    internal static int Stage(TrackingStatus status) => status switch
    {
        TrackingStatus.Created => 0,
        TrackingStatus.PickedUp => 1,
        TrackingStatus.InTransit => 2,
        TrackingStatus.OutForDelivery => 3,
        TrackingStatus.Delivered or TrackingStatus.Failed => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
