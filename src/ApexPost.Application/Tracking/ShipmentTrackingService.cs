using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tracking;

/// <summary>
/// The tracking API's use cases. Every business rule is here or in <see cref="TrackedShipment"/>, so the controllers
/// only bind input and choose the HTTP result. Each command commits once through the unit of work.
/// <list type="bullet">
/// <item>Invalid values (a bad tracking number, weight or phone) throw <see cref="ArgumentException"/>.</item>
/// <item>An unknown tracking number throws <see cref="TrackedShipmentNotFoundException"/>.</item>
/// <item>A rule the current state forbids throws a <see cref="ConflictException"/>.</item>
/// </list>
/// </summary>
public sealed class ShipmentTrackingService
{
    private readonly ITrackedShipmentRepository _shipments;
    private readonly ITrackingQueries _queries;
    private readonly IUnitOfWork _unitOfWork;

    public ShipmentTrackingService(ITrackedShipmentRepository shipments, ITrackingQueries queries, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _shipments = shipments;
        _queries = queries;
        _unitOfWork = unitOfWork;
    }

    public async Task<TrackedShipmentView> CreateAsync(NewTrackedShipment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var shipment = new TrackedShipment(
            new TrackingNumber(request.TrackingNumber),
            new Recipient(request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.City),
            request.WeightKg);
        if (await _shipments.FindAsync(shipment.TrackingNumber, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new TrackingNumberInUseException(shipment.TrackingNumber.Value);
        }

        _shipments.Add(shipment);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateKeyException exception)
        {
            // Two creates with the same number raced past the check above; the unique key stopped the second.
            throw new TrackingNumberInUseException(shipment.TrackingNumber.Value, exception);
        }

        return TrackedShipmentView.From(shipment);
    }

    public async Task<TrackedShipmentView> GetAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var number = new TrackingNumber(trackingNumber);
        return await _queries.GetAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    public Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1, nameof(query.Page));
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1, nameof(query.PageSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, TrackingListQuery.MaximumPageSize, nameof(query.PageSize));
        if (query.Status is { } status && !Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(query.Status));

        var city = string.IsNullOrWhiteSpace(query.City) ? null : query.City.Trim();
        return _queries.ListAsync(query with { City = city }, cancellationToken);
    }

    public async Task<TrackedShipmentView> ReplaceDetailsAsync(string trackingNumber, TrackedShipmentDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var recipient = new Recipient(details.RecipientName, details.RecipientPhone, details.RecipientAddress, details.City);
        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        if (shipment.IsFinished) throw new ShipmentFinishedException(shipment.Status);

        shipment.ReplaceDetails(recipient, details.WeightKg);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(shipment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackedShipmentView> ChangeStatusAsync(string trackingNumber, TrackingStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        if (!shipment.CanMoveTo(status))
        {
            throw new InvalidStatusTransitionException(shipment.Status, status, TrackingStatusRules.AllowedAfter(shipment.Status));
        }

        shipment.MoveTo(status);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(shipment, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        if (!shipment.CanBeDeleted) throw new ShipmentNotDeletableException(shipment.Status);

        _shipments.Remove(shipment);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrackingScanView>> GetScanHistoryAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var number = new TrackingNumber(trackingNumber);
        return await _queries.GetScanHistoryAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    private async Task<TrackedShipment> LoadAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var number = new TrackingNumber(trackingNumber);
        return await _shipments.FindAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    // The answer to an update includes the latest scan, which the loaded aggregate does not hold.
    private async Task<TrackedShipmentView> ViewAsync(TrackedShipment shipment, CancellationToken cancellationToken) =>
        await _queries.GetAsync(shipment.TrackingNumber, cancellationToken).ConfigureAwait(false) ?? TrackedShipmentView.From(shipment);
}
