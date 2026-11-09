using ApexPost.Api.Routing;
using Microsoft.AspNetCore.Routing;

namespace ApexPost.Api.Tests.Routing;

public sealed class TrackingNumberRouteConstraintTests
{
    private static readonly TrackingNumberRouteConstraint Constraint = new();

    [Theory]
    [InlineData("APX-24000001")]
    [InlineData("APX-00000000")]
    [InlineData("apx-24000001")]
    public void Accepts_apx_and_eight_ascii_digits(string value)
    {
        Assert.True(Match(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("APX-2400000")]
    [InlineData("APX-240000011")]
    [InlineData("APX24000001")]
    [InlineData("ABC-24000001")]
    [InlineData(" APX-24000001")]
    [InlineData("APX-24000001 ")]
    [InlineData("APX-24000001\n")]
    [InlineData("APX-٢٤٠٠٠٠٠١")]
    [InlineData("APX-24000001' OR '1'='1")]
    public void Rejects_anything_else(string value)
    {
        Assert.False(Match(value));
    }

    [Fact]
    public void Rejects_a_missing_value()
    {
        Assert.False(Constraint.Match(null, null, "trackingNumber", new RouteValueDictionary(), RouteDirection.IncomingRequest));
    }

    private static bool Match(string value) =>
        Constraint.Match(null, null, "trackingNumber", new RouteValueDictionary { ["trackingNumber"] = value }, RouteDirection.IncomingRequest);
}
