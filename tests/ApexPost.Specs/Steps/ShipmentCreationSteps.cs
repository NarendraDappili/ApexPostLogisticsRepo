using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Specs.Drivers;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.Steps;

/// <summary>Steps of ShipmentCreation.feature. Thin: each step calls the driver and checks one business outcome.</summary>
[Binding]
public sealed class ShipmentCreationSteps(HeadOfficeDriver headOffice, ScenarioState state)
{
    private HttpClient SignedIn => state.Client ?? throw new InvalidOperationException("Nobody is signed in at head office yet.");

    [Given(@"^an operations administrator is signed in at head office$")]
    public void GivenAnAdministratorIsSignedIn() => state.Client = headOffice.Administrator();

    [Given(@"^courier ""(.*)"" is signed in at head office$")]
    public void GivenACourierIsSignedInAtHeadOffice(string courierId) => state.Client = headOffice.Courier(courierId);

    [Given(@"^shipment ""(.*)"" has been registered for ""(.*)""$")]
    public async Task GivenAShipmentHasBeenRegistered(string trackingNumber, string recipientName)
    {
        using var response = await headOffice.RegisterAsync(headOffice.Administrator(), trackingNumber, recipientName);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [When(@"^the administrator registers shipment ""(.*)"" for ""(.*)"" in ""(.*)"" weighing (.*) kg$")]
    public async Task WhenTheAdministratorRegistersAShipment(string trackingNumber, string recipientName, string city, decimal weightKg)
    {
        state.TrackingNumber = trackingNumber;
        state.LastResponse = await headOffice.RegisterAsync(SignedIn, trackingNumber, recipientName, city, weightKg);
    }

    [When(@"^the administrator registers a shipment weighing (.*) kg$")]
    public async Task WhenTheAdministratorRegistersAShipmentWeighing(decimal weightKg)
    {
        state.TrackingNumber = headOffice.NextTrackingNumber();
        state.LastResponse = await headOffice.RegisterAsync(SignedIn, state.TrackingNumber, weightKg: weightKg);
    }

    [When(@"^the courier registers shipment ""(.*)""$")]
    public async Task WhenTheCourierRegistersAShipment(string trackingNumber)
    {
        state.TrackingNumber = trackingNumber;
        state.LastResponse = await headOffice.RegisterAsync(SignedIn, trackingNumber);
    }

    [Then(@"^the shipment is registered with the status ""(.*)""$")]
    public async Task ThenTheShipmentIsRegistered(string status)
    {
        var response = Answer();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/shipments/{state.Parcel}", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetString());
    }

    [Then(@"^the registration is accepted$")]
    public void ThenTheRegistrationIsAccepted() => Assert.Equal(HttpStatusCode.Created, Answer().StatusCode);

    [Then(@"^the registration is refused because of the weight$")]
    public async Task ThenTheRegistrationIsRefusedBecauseOfTheWeight()
    {
        var response = Answer();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), field => field.Name.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }

    [Then(@"^the registration is refused because the tracking number is already in use$")]
    public async Task ThenTheRegistrationIsRefusedAsADuplicate()
    {
        var response = Answer();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
        Assert.Contains($"tracking number {state.Parcel} already exists", detail, StringComparison.Ordinal);
    }

    [Then(@"^the registration is refused because only head office staff may register shipments$")]
    public void ThenTheRegistrationIsRefusedForACourier() => Assert.Equal(HttpStatusCode.Forbidden, Answer().StatusCode);

    [Then(@"^looking up ""(.*)"" shows ""(.*)"" in ""(.*)"" weighing (.*) kg$")]
    public async Task ThenLookingUpShows(string trackingNumber, string recipientName, string city, decimal weightKg)
    {
        var shipment = await headOffice.LookUpAsync(trackingNumber) ?? throw new Xunit.Sdk.XunitException($"Head office does not know {trackingNumber}.");
        var recipient = shipment.GetProperty("recipient");
        Assert.Equal((recipientName, city, weightKg), (recipient.GetProperty("name").GetString(), recipient.GetProperty("city").GetString(), shipment.GetProperty("weightKg").GetDecimal()));
    }

    [Then(@"^looking up ""(.*)"" still shows ""(.*)""$")]
    public async Task ThenLookingUpStillShows(string trackingNumber, string recipientName)
    {
        var shipment = await headOffice.LookUpAsync(trackingNumber) ?? throw new Xunit.Sdk.XunitException($"Head office does not know {trackingNumber}.");
        Assert.Equal(recipientName, shipment.GetProperty("recipient").GetProperty("name").GetString());
    }

    [Then(@"^head office does not know ""(.*)""$")]
    public async Task ThenHeadOfficeDoesNotKnow(string trackingNumber) => Assert.Null(await headOffice.LookUpAsync(trackingNumber));

    private HttpResponseMessage Answer() => state.LastResponse ?? throw new InvalidOperationException("No registration was attempted in this scenario.");
}
