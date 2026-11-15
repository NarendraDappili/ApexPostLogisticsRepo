namespace ApexPost.Specs.Support;

/// <summary>
/// Sets up and cleans up the test data around the scenarios:
/// <list type="bullet">
/// <item>Each feature gets its own head office, an in-memory API over a freshly migrated, throwaway SQL Server
/// database. The database is dropped when the feature ends.</item>
/// <item>Each scenario tagged <c>@courier</c> gets a fresh device, with a Couchbase Lite store in a new temporary
/// folder that is deleted when the scenario ends.</item>
/// </list>
/// Every scenario uses tracking numbers of its own, so scenarios never see each other's parcels.
/// </summary>
[Binding]
public sealed class Hooks(DeviceContext device, HeadOfficeContext headOffice)
{
    [BeforeFeature]
    public static void StartHeadOffice(FeatureContext feature)
    {
        var host = new HeadOffice();
        host.Start();
        feature.Set(host);
    }

    [AfterFeature]
    public static async Task CloseHeadOfficeAndDropItsDatabase(FeatureContext feature) => await feature.Get<HeadOffice>().StopAsync();

    [BeforeScenario("courier")]
    public void GiveTheCourierAFreshDevice() => device.Start(headOffice.Host);

    [AfterScenario("courier")]
    public void WipeTheDevice() => device.Dispose();
}
