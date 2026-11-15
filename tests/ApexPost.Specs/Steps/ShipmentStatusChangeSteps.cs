// Copilot-assisted: drafted by GitHub Copilot from Prompt 2, then reviewed and changed by hand. Each decision and its
// reason is in COPILOT-LOG.md, section 3.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Specs.Drivers;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.Steps;

/// <summary>Steps of ShipmentStatusChange.feature. The sign-in steps are ShipmentCreationSteps'; Reqnroll shares bindings across features.</summary>
[Binding]
public sealed class ShipmentStatusChangeSteps(HeadOfficeDriver headOffice, ScenarioState state)
{
    // Review: Copilot redefined "an operations administrator is signed in at head office" and "courier … is signed
    // in at head office" here. Those steps already exist in ShipmentCreationSteps, so Reqnroll found every one of them
    // bound twice (AmbiguousBindingException). That failed 18 of 23 scenarios, including all 7 hand-written
    // ShipmentCreation scenarios. Both copies were removed, and the existing ones are reused.
    private HttpClient SignedIn =>
        state.Client ?? throw new InvalidOperationException("Nobody is signed in at head office yet.");

    // Review: kept.
    [Given(@"^shipment ""(.*)"" is at status ""(.*)""$")]
    public async Task GivenShipmentIsAtStatus(string trackingNumber, string status)
    {
        state.TrackingNumber = trackingNumber;
        await headOffice.RegisterAtAsync(trackingNumber, status);
    }

    // Review: changed. Copilot had two identical methods, one for "the courier" and one for "the administrator", and
    // each called PATCH itself. One binding now covers both actors, through the driver's new ChangeStatusAsync, so
    // the HTTP shape lives in one place. The prompt asked for the driver to be used.
    [When(@"^the (?:courier|administrator) changes shipment ""(.*)"" to status ""(.*)""$")]
    public async Task WhenTheSignedInStaffMemberChangesTheStatus(string trackingNumber, string status)
    {
        state.TrackingNumber = trackingNumber;
        state.LastResponse = await headOffice.ChangeStatusAsync(SignedIn, trackingNumber, status);
    }

    // Review: changed. "has status" and "remains at status" were two identical methods; one method now has both phrasings.
    [Then(@"^shipment ""(.*)"" has status ""(.*)""$")]
    [Then(@"^shipment ""(.*)"" remains at status ""(.*)""$")]
    public async Task ThenTheShipmentHasTheStatus(string trackingNumber, string expectedStatus)
    {
        var shipment = await headOffice.LookUpAsync(trackingNumber) ?? throw new Xunit.Sdk.XunitException($"Head office does not know {trackingNumber}.");
        Assert.Equal(expectedStatus, shipment.GetProperty("status").GetString());
    }

    // Review: changed. A wrong expected value: Copilot expected 400 Bad Request. A refused move is a valid request
    // that conflicts with the shipment's current state, which the API answers with 409 Conflict (ADR 0026). All 3 rows
    // failed on it. The detail check was kept and made exact by the feature's example ("Delivered or Failed").
    [Then(@"^the change is refused and the next allowed status is ""(.*)""$")]
    public async Task ThenTheChangeIsRefusedNamingTheNextStatus(string allowedStatus)
    {
        var detail = await RefusedDetailAsync();
        Assert.EndsWith($"The next status can only be {allowedStatus}.", detail, StringComparison.Ordinal);
    }

    // Review: changed. The same wrong 400. The step now also checks the reason the courier is given.
    [Then(@"^the change is refused because the status is final$")]
    public async Task ThenTheChangeIsRefusedBecauseTheStatusIsFinal()
    {
        var detail = await RefusedDetailAsync();
        Assert.EndsWith("and its status cannot change.", detail, StringComparison.Ordinal);
    }

    // Review: kept.
    [Then(@"^the status change is refused because the staff member is not a courier$")]
    public void ThenTheStatusChangeIsRefusedForNonCourier() =>
        Assert.Equal(HttpStatusCode.Forbidden, LastResponse().StatusCode);

    private async Task<string?> RefusedDetailAsync()
    {
        var response = LastResponse();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
    }

    private HttpResponseMessage LastResponse() =>
        state.LastResponse ?? throw new InvalidOperationException("No status change was attempted in this scenario.");
}
