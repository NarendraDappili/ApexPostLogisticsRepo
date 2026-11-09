using ApexPost.Application.Abstractions;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Tracking;

// Not found is a 404 and every conflict a 409 (ApplicationErrors.cs); the API maps the base classes.

public sealed class TrackedShipmentNotFoundException(string trackingNumber)
    : NotFoundException($"No shipment has the tracking number {trackingNumber}.")
{
    public string TrackingNumber { get; } = trackingNumber;
}

public sealed class TrackingNumberInUseException(string trackingNumber, Exception? innerException = null)
    : ConflictException($"A shipment with the tracking number {trackingNumber} already exists.", innerException)
{
    public string TrackingNumber { get; } = trackingNumber;
}

public sealed class InvalidStatusTransitionException(TrackingStatus from, TrackingStatus to, IReadOnlyList<TrackingStatus> allowed)
    : ConflictException(allowed.Count == 0
        ? $"The shipment is already {from} and its status cannot change."
        : $"A shipment cannot move from {from} to {to}. The next status can only be {string.Join(" or ", allowed)}.")
{
    public TrackingStatus From { get; } = from;

    public TrackingStatus To { get; } = to;

    public IReadOnlyList<TrackingStatus> Allowed { get; } = allowed;
}

public sealed class ShipmentNotDeletableException(TrackingStatus status)
    : ConflictException($"Only a shipment in Created status can be deleted; this one is {status}.")
{
    public TrackingStatus Status { get; } = status;
}

public sealed class ShipmentFinishedException(TrackingStatus status)
    : ConflictException($"The shipment is {status}; its details can no longer change.")
{
    public TrackingStatus Status { get; } = status;
}
