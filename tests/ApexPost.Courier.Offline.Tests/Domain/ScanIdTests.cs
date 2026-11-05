using System.Text.RegularExpressions;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Tests.Domain;

public class ScanIdTests
{
    [Fact]
    public void New_ids_use_the_scan_prefix_and_a_guid()
    {
        var id = ScanId.New();

        Assert.Matches(new Regex("^scan::[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"), id.Value);
        Assert.Equal(id.Value, id.ToString());
    }

    [Fact]
    public void New_ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => ScanId.New()).ToHashSet();

        Assert.Equal(1000, ids.Count);
    }

    [Fact]
    public void Parse_round_trips_and_ignores_guid_case()
    {
        var id = ScanId.New();

        Assert.Equal(id, ScanId.Parse(id.Value));
        Assert.Equal(id, ScanId.Parse(id.Value.ToUpperInvariant().Replace("SCAN::", "scan::", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("scan::")]
    [InlineData("scan::not-a-guid")]
    [InlineData("SCAN::0190a0f2-7d3c-7b1e-9a4d-2f6c8e1b3a5d")]
    [InlineData("parcel::0190a0f2-7d3c-7b1e-9a4d-2f6c8e1b3a5d")]
    [InlineData("0190a0f2-7d3c-7b1e-9a4d-2f6c8e1b3a5d")]
    [InlineData("scan::{0190a0f2-7d3c-7b1e-9a4d-2f6c8e1b3a5d}")]
    [InlineData("scan::00000000-0000-0000-0000-000000000000")]
    [InlineData("scan::0190a0f2-7d3c-7b1e-9a4d-2f6c8e1b3a5d ")]
    public void TryParse_rejects_anything_but_scan_prefix_and_guid(string? value)
    {
        Assert.False(ScanId.TryParse(value, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Parse_throws_a_format_exception_for_an_invalid_id()
    {
        Assert.Throws<FormatException>(() => ScanId.Parse("scan::nope"));
    }
}
