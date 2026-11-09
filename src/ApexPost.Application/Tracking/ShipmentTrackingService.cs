using System.Text.RegularExpressions;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tracking;

/// <summary>
/// The tracking API's use cases. Every business rule is here or in <see cref="TrackedShipment"/>, so the controllers
/// only bind input and choose the HTTP result. Each command commits once through the unit of work. The errors are
/// typed, so a caller maps them without looking at messages or stack traces:
/// <list type="bullet">
/// <item>An invalid value (a bad tracking number, weight, phone or page) throws <see cref="RequestValidationException"/>.</item>
/// <item>An unknown tracking number throws <see cref="TrackedShipmentNotFoundException"/>.</item>
/// <item>A rule the current state forbids throws a <see cref="ConflictException"/>.</item>
/// </list>
/// </summary>
public sealed partial class ShipmentTrackingService
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
        var shipment = Input(() => new TrackedShipment(
            new TrackingNumber(request.TrackingNumber),
            new Recipient(request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.City),
            request.WeightKg));
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
        var number = Number(trackingNumber);
        return await _queries.GetAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    public Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = new List<string>();
        if (query.Page < 1) errors.Add("page must be 1 or more.");
        if (query.PageSize is < 1 or > TrackingListQuery.MaximumPageSize) errors.Add($"pageSize must be between 1 and {TrackingListQuery.MaximumPageSize}.");
        if (query.Status is { } status && !Enum.IsDefined(status)) errors.Add("status is not a known shipment status.");
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var city = string.IsNullOrWhiteSpace(query.City) ? null : query.City.Trim();
        return _queries.ListAsync(query with { City = city }, cancellationToken);
    }

    public async Task<TrackedShipmentView> ReplaceDetailsAsync(string trackingNumber, TrackedShipmentDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var recipient = Input(() => new Recipient(details.RecipientName, details.RecipientPhone, details.RecipientAddress, details.City));
        if (!TrackedShipment.IsValidWeight(details.WeightKg)) throw new RequestValidationException([WeightError]);
        var shipment = await LoadAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        if (shipment.IsFinished) throw new ShipmentFinishedException(shipment.Status);

        shipment.ReplaceDetails(recipient, details.WeightKg);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(shipment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackedShipmentView> ChangeStatusAsync(string trackingNumber, TrackingStatus status, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status)) throw new RequestValidationException(["status is not a known shipment status."]);
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
        var number = Number(trackingNumber);
        return await _queries.GetScanHistoryAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    private static readonly string WeightError = $"weightKg: The weight must be between {TrackedShipment.MinimumWeightKg} and {TrackedShipment.MaximumWeightKg} kg.";

    private async Task<TrackedShipment> LoadAsync(string trackingNumber, CancellationToken cancellationToken)
    {
        var number = Number(trackingNumber);
        return await _shipments.FindAsync(number, cancellationToken).ConfigureAwait(false)
            ?? throw new TrackedShipmentNotFoundException(number.Value);
    }

    // The answer to an update includes the latest scan, which the loaded aggregate does not hold.
    private async Task<TrackedShipmentView> ViewAsync(TrackedShipment shipment, CancellationToken cancellationToken) =>
        await _queries.GetAsync(shipment.TrackingNumber, cancellationToken).ConfigureAwait(false) ?? TrackedShipmentView.From(shipment);

    private static TrackingNumber Number(string trackingNumber) => Input(() => new TrackingNumber(trackingNumber));

    // Domain parameter names as the API's request fields are called. "value" is the tracking number's constructor.
    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.Ordinal)
    {
        ["value"] = "trackingNumber",
        ["name"] = "recipientName",
        ["phone"] = "recipientPhone",
        ["address"] = "recipientAddress",
    };

    /// <summary>
    /// Builds domain values from caller input. Only value constructors run inside, so an ArgumentException here is
    /// always a bad input value, reported as a validation error that names the request field.
    /// </summary>
    private static T Input<T>(Func<T> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception) when (exception is not RequestValidationException)
        {
            var message = ParameterSuffix().Replace(exception.Message, string.Empty);
            var field = exception.ParamName is { Length: > 0 } name ? FieldNames.GetValueOrDefault(name, name) : null;
            throw new RequestValidationException([field is null ? message : $"{field}: {message}"]);
        }
    }

    // "The weight must be between 0.1 and 70 kg. (Parameter 'weightKg')\nActual value was 80." -> "The weight must be ..."
    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)(\r?\nActual value was .*)?$", RegexOptions.Singleline)]
    private static partial Regex ParameterSuffix();
}
