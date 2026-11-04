using System.Diagnostics;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Simulation;

namespace ApexPost.Infrastructure.Tests.Simulation;

/// <summary>The simulated courier and identity provider the demo runs against; no database or network is used.</summary>
public sealed class SimulatedExternalServiceTests
{
    [Fact]
    public async Task The_courier_reports_the_same_status_for_the_same_shipment_every_time()
    {
        using var courier = new SimulatedCourierTrackingClient(new SimulatedCourierOptions { LatencyMilliseconds = 0 });

        var first = await courier.GetStatusAsync(new ShipmentId("SHP-STABLE-1"));
        var again = await courier.GetStatusAsync(new ShipmentId("shp-stable-1"));

        Assert.Equal(first, again);
        Assert.Contains(first, new[] { "Accepted at counter", "In transit", "Out for delivery", "Delivered" });
    }

    [Fact]
    public async Task Lookups_beyond_the_concurrency_limit_wait_their_turn_instead_of_running_in_parallel()
    {
        using var courier = new SimulatedCourierTrackingClient(new SimulatedCourierOptions { LatencyMilliseconds = 150, MaxConcurrentLookups = 1 });
        var watch = Stopwatch.StartNew();

        await Task.WhenAll(courier.GetStatusAsync(new ShipmentId("SHP-1")), courier.GetStatusAsync(new ShipmentId("SHP-2")));

        // One at a time: two lookups of 150 ms each take at least 300 ms together.
        Assert.True(watch.ElapsedMilliseconds >= 280, $"The two lookups overlapped ({watch.ElapsedMilliseconds} ms).");
    }

    [Fact]
    public async Task A_lookup_waiting_for_a_free_slot_can_be_cancelled_and_does_not_take_the_slot()
    {
        using var courier = new SimulatedCourierTrackingClient(new SimulatedCourierOptions { LatencyMilliseconds = 300, MaxConcurrentLookups = 1 });
        var running = courier.GetStatusAsync(new ShipmentId("SHP-1"));
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => courier.GetStatusAsync(new ShipmentId("SHP-2"), giveUp.Token));

        Assert.NotNull(await running);
        // The cancelled waiter released nothing it did not hold: the next lookup still gets the one slot.
        Assert.NotNull(await courier.GetStatusAsync(new ShipmentId("SHP-3")));
    }

    [Fact]
    public void The_courier_refuses_a_negative_latency_or_no_lookup_slots()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCourierTrackingClient(new SimulatedCourierOptions { LatencyMilliseconds = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedCourierTrackingClient(new SimulatedCourierOptions { MaxConcurrentLookups = 0 }));
        Assert.Throws<ArgumentNullException>(() => new SimulatedCourierTrackingClient(null!));
    }

    [Theory]
    [InlineData("Passport", "AB123456", IdentityVerificationOutcome.Verified)]
    [InlineData("passport", "AB123456", IdentityVerificationOutcome.Verified)]
    [InlineData("Library card", "AB123456", IdentityVerificationOutcome.Failed)]
    [InlineData("Passport", "AB12", IdentityVerificationOutcome.Failed)]
    [InlineData("Passport", "AB-123456", IdentityVerificationOutcome.Failed)]
    [InlineData("Passport", "AB120000", IdentityVerificationOutcome.Failed)]
    public async Task The_identity_provider_verifies_only_a_supported_document_with_a_plausible_number(string documentType, string number, IdentityVerificationOutcome outcome)
    {
        Assert.Equal(outcome, await new SimulatedIdentityVerifier().VerifyAsync(documentType, number));
    }
}
