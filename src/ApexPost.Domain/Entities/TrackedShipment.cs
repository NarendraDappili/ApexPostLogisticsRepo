using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

/// <summary>
/// A shipment as the tracking API and the couriers see it: a public tracking number, the recipient, the weight and
/// where it is (<see cref="Status"/>). It is separate from the counter's <see cref="Shipment"/>, which is priced
/// and billed at a branch and has no recipient or status.
/// <list type="bullet">
/// <item>The status only moves along <see cref="TrackingStatusRules"/>.</item>
/// <item>The details can be replaced until the shipment is Delivered or Failed.</item>
/// <item>It can be deleted only while it is still Created, before any courier has handled it.</item>
/// </list>
/// </summary>
public sealed class TrackedShipment
{
    public const decimal MinimumWeightKg = 0.1m;
    public const decimal MaximumWeightKg = 70m;

    public TrackedShipment(TrackingNumber trackingNumber, Recipient recipient, decimal weightKg)
    {
        ArgumentNullException.ThrowIfNull(trackingNumber);
        ArgumentNullException.ThrowIfNull(recipient);

        TrackingNumber = trackingNumber;
        Recipient = recipient;
        WeightKg = CheckWeight(weightKg);
        Status = TrackingStatus.Created;
    }

    // Used only when a stored shipment is materialized; the recipient is loaded afterwards as an owned value.
    private TrackedShipment(TrackingNumber trackingNumber, decimal weightKg, TrackingStatus status)
    {
        TrackingNumber = trackingNumber;
        Recipient = null!;
        WeightKg = weightKg;
        Status = status;
    }

    public TrackingNumber TrackingNumber { get; }

    public Recipient Recipient { get; private set; }

    public decimal WeightKg { get; private set; }

    public TrackingStatus Status { get; private set; }

    /// <summary>Delivered or Failed: nothing about the shipment changes any more.</summary>
    public bool IsFinished => TrackingStatusRules.IsFinal(Status);

    public bool CanBeDeleted => Status == TrackingStatus.Created;

    /// <summary>Replaces the recipient and the weight; refused once the shipment is finished.</summary>
    public void ReplaceDetails(Recipient recipient, decimal weightKg)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var checkedWeight = CheckWeight(weightKg);
        if (IsFinished) throw new InvalidOperationException($"The shipment is {Status}; its details can no longer change.");

        // An equal value keeps the stored instance, so an unchanged recipient is not written or audited as a change.
        if (recipient != Recipient) Recipient = recipient;
        WeightKg = checkedWeight;
    }

    public bool CanMoveTo(TrackingStatus status) => TrackingStatusRules.IsAllowed(Status, status);

    public void MoveTo(TrackingStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!CanMoveTo(status)) throw new InvalidOperationException(DescribeRefusedMove(status));
        Status = status;
    }

    /// <summary>
    /// Whether a scan of this type is accepted, and the status it leaves the shipment in:
    /// <list type="bullet">
    /// <item>a scan for the next allowed status moves the shipment there;</item>
    /// <item>a repeat of the current status (a second hub's InTransit scan) is history only, unless the shipment is finished;</item>
    /// <item>a scan for a stage the shipment has already passed (a pickup scan uploaded late by an offline device) is
    /// history only, so the status never goes backwards;</item>
    /// <item>anything else (skipping a stage, or changing a finished shipment) is refused with the reason.</item>
    /// </list>
    /// </summary>
    public ScanDecision EvaluateScan(TrackingScanType scanType)
    {
        var target = TrackingStatusRules.StatusFor(scanType);
        if (CanMoveTo(target)) return ScanDecision.Accept(target);
        if (target == Status && !IsFinished) return ScanDecision.Accept(Status);
        if (TrackingStatusRules.Stage(target) < TrackingStatusRules.Stage(Status)) return ScanDecision.Accept(Status);
        return ScanDecision.Refuse(Status, $"A {scanType} scan cannot be recorded: {DescribeRefusedMove(target)}");
    }

    /// <summary>Records an accepted scan and applies its status; throws with the reason when the scan is refused.</summary>
    public TrackingScan RecordScan(TrackingScanId scanId, TrackingScanType scanType, string location, string courierId, DateTimeOffset scannedAtUtc, string? notes, DateTimeOffset receivedAtUtc)
    {
        if (!Enum.IsDefined(scanType)) throw new ArgumentOutOfRangeException(nameof(scanType));
        var decision = EvaluateScan(scanType);
        if (!decision.Accepted) throw new InvalidOperationException(decision.RefusalReason);

        var scan = new TrackingScan(scanId, TrackingNumber, scanType, location, courierId, scannedAtUtc, notes, receivedAtUtc);
        Status = decision.StatusAfter;
        return scan;
    }

    private string DescribeRefusedMove(TrackingStatus status)
    {
        var allowed = TrackingStatusRules.AllowedAfter(Status);
        return allowed.Count == 0
            ? $"the shipment is already {Status} and cannot change."
            : $"a shipment cannot move from {Status} to {status}; the next status can only be {string.Join(" or ", allowed)}.";
    }

    public static bool IsValidWeight(decimal weightKg) => weightKg is >= MinimumWeightKg and <= MaximumWeightKg;

    private static decimal CheckWeight(decimal weightKg) =>
        IsValidWeight(weightKg)
            ? weightKg
            : throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, $"The weight must be between {MinimumWeightKg} and {MaximumWeightKg} kg.");
}

/// <summary>The outcome of <see cref="TrackedShipment.EvaluateScan"/>.</summary>
public readonly record struct ScanDecision(bool Accepted, TrackingStatus StatusAfter, string? RefusalReason)
{
    internal static ScanDecision Accept(TrackingStatus statusAfter) => new(true, statusAfter, null);

    internal static ScanDecision Refuse(TrackingStatus status, string reason) => new(false, status, reason);
}
