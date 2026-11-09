using System.Text.Json;
using System.Text.Json.Nodes;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// The app keeps its own copies of the v2 bodies it sends and reads. These tests hold them against the committed
/// contract, contracts/v2.json, so a change on either side that would break the upload fails here, not on a courier's
/// device.
/// </summary>
public sealed class ContractCompatibilityTests
{
    private static readonly JsonNode Schemas = JsonNode.Parse(File.ReadAllText(RepositoryFiles.PathOf("contracts", "v2.json")))!["components"]!["schemas"]!;

    [Fact]
    public void A_scan_is_sent_with_exactly_the_fields_the_api_takes_and_every_required_one()
    {
        var scan = ShipmentScan.Record("APX-30000001", ScanType.Delivered, "CR-1042", "14 Elm Street", DateTimeOffset.UtcNow, null);
        var sent = JsonNode.Parse(JsonSerializer.Serialize(ScanUploadItem.From(scan), CourierJsonContext.Default.ScanUploadItem))!.AsObject();
        var schema = Schemas["ScanUploadItem"]!;

        Assert.Equal(Names(schema["properties"]!.AsObject()), Names(sent));
        Assert.All(schema["required"]!.AsArray().Select(name => name!.GetValue<string>()), name => Assert.NotNull(sent[name]));
    }

    [Fact]
    public void Every_scan_type_the_app_records_is_one_the_api_knows_by_name()
    {
        var known = Schemas["TrackingScanType"]!["enum"]!.AsArray().Select(value => value!.ToString()).ToList();

        Assert.All(Enum.GetNames<ScanType>(), name => Assert.Contains(name, known));
    }

    [Fact]
    public void The_app_reads_every_field_of_the_upload_result_it_relies_on()
    {
        var api = Names(Schemas["ScanUploadResponse"]!["properties"]!.AsObject());
        var app = typeof(ScanUploadResult).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(api, app);
        Assert.Equal(["reason", "scanId"], Names(Schemas["ScanRejectionResponse"]!["properties"]!.AsObject()));
    }

    private static List<string> Names(JsonObject json) => [.. json.Select(property => property.Key).Order(StringComparer.Ordinal)];
}
