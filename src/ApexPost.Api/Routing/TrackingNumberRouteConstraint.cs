using System.Globalization;
using System.Text.RegularExpressions;

namespace ApexPost.Api.Routing;

/// <summary>
/// Route constraint <c>{trackingNumber:trackingNumber}</c>: the segment must look like <c>APX-24000001</c>. Letters
/// may be in either case (the service normalises them); the digits must be ASCII, because <c>\d</c> would also
/// accept other scripts' digits, and the pattern ends at <c>\z</c> because <c>$</c> also matches before a final line
/// break. A segment that does not match is a 404 without running any action.
/// </summary>
internal sealed partial class TrackingNumberRouteConstraint : IRouteConstraint
{
    public const string Name = "trackingNumber";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var value)
        && Convert.ToString(value, CultureInfo.InvariantCulture) is { } text
        && Pattern().IsMatch(text);

    [GeneratedRegex(@"^APX-[0-9]{8}\z",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
