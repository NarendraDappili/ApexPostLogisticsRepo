using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Tracking;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Tracking;

public sealed class ShipmentTrackingServiceTests
{
    private readonly InMemoryTrackingStore _store = new();
    private readonly SpyUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Create_stores_a_created_shipment_and_commits_once()
    {
        var view = await Service().CreateAsync(NewShipment(" apx-24000001 "));

        Assert.Equal(("APX-24000001", TrackingStatus.Created, 5m, "Portside", null as TrackingScanView), (view.TrackingNumber, view.Status, view.WeightKg, view.City, view.LatestScan));
        Assert.NotNull(_store.Find("APX-24000001"));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Create_refuses_a_tracking_number_in_use_without_committing()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        var exception = await Assert.ThrowsAsync<TrackingNumberInUseException>(() => Service().CreateAsync(NewShipment("APX-24000001")));

        Assert.IsAssignableFrom<ConflictException>(exception);
        Assert.Equal(0, _unitOfWork.Attempts);
    }

    [Fact]
    public async Task Create_turns_a_duplicate_key_race_into_the_same_conflict()
    {
        var unitOfWork = new SpyUnitOfWork { FailWith = new DuplicateKeyException("duplicate") };
        var service = new ShipmentTrackingService(_store, _store, unitOfWork);

        var exception = await Assert.ThrowsAsync<TrackingNumberInUseException>(() => service.CreateAsync(NewShipment("APX-24000001")));

        Assert.IsType<DuplicateKeyException>(exception.InnerException);
    }

    [Theory]
    [InlineData("APX-1", 5, "0207946000")]
    [InlineData("APX-24000001", 70.5, "0207946000")]
    [InlineData("APX-24000001", 5, "phone")]
    public async Task Create_refuses_invalid_values_before_touching_the_store(string trackingNumber, double weight, string phone)
    {
        var request = NewShipment(trackingNumber) with { WeightKg = (decimal)weight, RecipientPhone = phone };

        await Assert.ThrowsAsync<RequestValidationException>(() => Service().CreateAsync(request));

        Assert.Equal(0, _unitOfWork.Attempts);
        Assert.Equal(0, _store.Count);
    }

    [Theory]
    [InlineData("APX-1", 5, "0207946000", "trackingNumber: A tracking number must be APX- followed by eight digits, for example APX-24000001.")]
    [InlineData("APX-24000001", 80, "0207946000", "weightKg: The weight must be between 0.1 and 70 kg.")]
    [InlineData("APX-24000001", 5, "12", "recipientPhone: A phone number must have 7 to 15 digits and may contain only +, spaces, hyphens and brackets.")]
    public async Task Invalid_values_are_reported_by_field_without_exception_noise(string trackingNumber, double weight, string phone, string expected)
    {
        var request = NewShipment(trackingNumber) with { WeightKg = (decimal)weight, RecipientPhone = phone };

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => Service().CreateAsync(request));

        Assert.Equal([expected], exception.Errors);
    }

    [Fact]
    public async Task Replace_details_refuses_an_invalid_weight_before_loading()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Service().ReplaceDetailsAsync("APX-24000001", new TrackedShipmentDetails("Kofi Mensah", "0207946001", "3 Quay Street", "Rivermouth", 0m)));

        Assert.Equal(["weightKg: The weight must be between 0.1 and 70 kg."], exception.Errors);
    }

    [Fact]
    public async Task List_reports_every_invalid_value_at_once()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => Service().ListAsync(new TrackingListQuery(0, 51, (TrackingStatus)42)));

        Assert.Equal(["page must be 1 or more.", "pageSize must be between 1 and 50.", "status is not a known shipment status."], exception.Errors);
    }

    [Fact]
    public async Task Get_returns_the_view_or_a_not_found_error()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        Assert.Equal("APX-24000001", (await Service().GetAsync("apx-24000001")).TrackingNumber);
        var missing = await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() => Service().GetAsync("APX-99999999"));
        Assert.Equal("APX-99999999", missing.TrackingNumber);
        Assert.IsAssignableFrom<NotFoundException>(missing);
    }

    [Fact]
    public async Task List_passes_the_trimmed_filter_to_the_query()
    {
        await Service().ListAsync(new TrackingListQuery(2, 10, TrackingStatus.InTransit, "  Portside "));

        Assert.Equal(new TrackingListQuery(2, 10, TrackingStatus.InTransit, "Portside"), _store.LastListQuery);
    }

    [Fact]
    public async Task List_treats_a_blank_city_as_no_filter()
    {
        await Service().ListAsync(new TrackingListQuery(City: "   "));

        Assert.Null(_store.LastListQuery!.City);
        Assert.Equal(TrackingListQuery.DefaultPageSize, _store.LastListQuery.PageSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task List_refuses_a_page_outside_the_limits(int page, int pageSize)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().ListAsync(new TrackingListQuery(page, pageSize)));

        Assert.Null(_store.LastListQuery);
    }

    [Fact]
    public async Task List_refuses_an_unknown_status()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().ListAsync(new TrackingListQuery(Status: (TrackingStatus)42)));
    }

    [Fact]
    public async Task Replace_details_updates_the_shipment_and_commits_once()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        var view = await Service().ReplaceDetailsAsync("APX-24000001", new TrackedShipmentDetails("Kofi Mensah", "0207946001", "3 Quay Street", "Rivermouth", 12.5m));

        Assert.Equal(("Kofi Mensah", "Rivermouth", 12.5m), (view.RecipientName, view.City, view.WeightKg));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Replace_details_of_a_finished_shipment_is_a_conflict()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001", TrackingStatus.Delivered));

        var exception = await Assert.ThrowsAsync<ShipmentFinishedException>(() =>
            Service().ReplaceDetailsAsync("APX-24000001", new TrackedShipmentDetails("Kofi Mensah", "0207946001", "3 Quay Street", "Rivermouth", 1m)));

        Assert.Equal(TrackingStatus.Delivered, exception.Status);
        Assert.Equal(0, _unitOfWork.Attempts);
    }

    [Fact]
    public async Task Replace_details_of_an_unknown_shipment_is_not_found()
    {
        await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() =>
            Service().ReplaceDetailsAsync("APX-24000001", new TrackedShipmentDetails("Kofi Mensah", "0207946001", "3 Quay Street", "Rivermouth", 1m)));
    }

    [Fact]
    public async Task Change_status_moves_to_the_next_status_and_commits_once()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        var view = await Service().ChangeStatusAsync("APX-24000001", TrackingStatus.PickedUp);

        Assert.Equal(TrackingStatus.PickedUp, view.Status);
        Assert.Equal(TrackingStatus.PickedUp, _store.Find("APX-24000001")!.Status);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Change_status_that_skips_a_stage_is_a_conflict_naming_the_allowed_status()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        var exception = await Assert.ThrowsAsync<InvalidStatusTransitionException>(() => Service().ChangeStatusAsync("APX-24000001", TrackingStatus.Delivered));

        Assert.Equal((TrackingStatus.Created, TrackingStatus.Delivered), (exception.From, exception.To));
        Assert.Equal([TrackingStatus.PickedUp], exception.Allowed);
        Assert.Equal("A shipment cannot move from Created to Delivered. The next status can only be PickedUp.", exception.Message);
        Assert.Equal(0, _unitOfWork.Attempts);
    }

    [Fact]
    public async Task Change_status_of_a_finished_shipment_says_it_cannot_change()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001", TrackingStatus.Failed));

        var exception = await Assert.ThrowsAsync<InvalidStatusTransitionException>(() => Service().ChangeStatusAsync("APX-24000001", TrackingStatus.Delivered));

        Assert.Empty(exception.Allowed);
        Assert.Equal("The shipment is already Failed and its status cannot change.", exception.Message);
    }

    [Fact]
    public async Task Change_status_refuses_an_undefined_status()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().ChangeStatusAsync("APX-24000001", (TrackingStatus)42));
    }

    [Fact]
    public async Task Delete_removes_a_created_shipment()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        await Service().DeleteAsync("APX-24000001");

        Assert.Null(_store.Find("APX-24000001"));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Theory]
    [InlineData(TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.Delivered)]
    public async Task Delete_after_created_is_a_conflict(TrackingStatus status)
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001", status));

        var exception = await Assert.ThrowsAsync<ShipmentNotDeletableException>(() => Service().DeleteAsync("APX-24000001"));

        Assert.Equal(status, exception.Status);
        Assert.NotNull(_store.Find("APX-24000001"));
        Assert.Equal(0, _unitOfWork.Attempts);
    }

    [Fact]
    public async Task Delete_of_an_unknown_shipment_is_not_found()
    {
        await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() => Service().DeleteAsync("APX-24000001"));
    }

    [Fact]
    public async Task Scan_history_is_empty_for_a_known_shipment_and_not_found_for_an_unknown_one()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

        Assert.Empty(await Service().GetScanHistoryAsync("APX-24000001"));
        await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() => Service().GetScanHistoryAsync("APX-24000002"));
    }

    [Fact]
    public void The_constructor_refuses_missing_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ShipmentTrackingService(null!, _store, _unitOfWork));
        Assert.Throws<ArgumentNullException>(() => new ShipmentTrackingService(_store, null!, _unitOfWork));
        Assert.Throws<ArgumentNullException>(() => new ShipmentTrackingService(_store, _store, null!));
    }

    private ShipmentTrackingService Service() => new(_store, _store, _unitOfWork);

    private static NewTrackedShipment NewShipment(string trackingNumber) =>
        new(trackingNumber, "Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside", 5m);
}

internal static class TrackedShipmentFactory
{
    /// <summary>A shipment walked along the status chain to <paramref name="status"/>.</summary>
    public static TrackedShipment Create(string trackingNumber, TrackingStatus status = TrackingStatus.Created)
    {
        var shipment = new TrackedShipment(new TrackingNumber(trackingNumber), new Recipient("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"), 5m);
        TrackingStatus[] path = status == TrackingStatus.Failed
            ? [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Failed]
            : [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered];
        foreach (var step in path)
        {
            if (shipment.Status == status) break;
            shipment.MoveTo(step);
        }

        return shipment;
    }
}

/// <summary>Repository and queries over one list, as the database would answer them; staged changes apply at once.</summary>
internal sealed class InMemoryTrackingStore : ITrackedShipmentRepository, ITrackingQueries
{
    private readonly List<TrackedShipment> _shipments = [];

    public TrackingListQuery? LastListQuery { get; private set; }

    public int Count => _shipments.Count;

    public void Seed(TrackedShipment shipment) => _shipments.Add(shipment);

    public TrackedShipment? Find(string trackingNumber) => _shipments.SingleOrDefault(shipment => shipment.TrackingNumber.Value == trackingNumber);

    public Task<TrackedShipment?> FindAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(trackingNumber.Value));

    public void Add(TrackedShipment shipment) => _shipments.Add(shipment);

    public void Remove(TrackedShipment shipment) => _shipments.Remove(shipment);

    public Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default)
    {
        LastListQuery = query;
        return Task.FromResult(new PagedResult<TrackedShipmentView>([.. _shipments.Select(shipment => TrackedShipmentView.From(shipment))], query.Page, query.PageSize, _shipments.Count));
    }

    public Task<TrackedShipmentView?> GetAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(trackingNumber.Value) is { } shipment ? TrackedShipmentView.From(shipment) : null);

    public Task<IReadOnlyList<TrackingScanView>?> GetScanHistoryAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TrackingScanView>?>(Find(trackingNumber.Value) is null ? null : []);
}
