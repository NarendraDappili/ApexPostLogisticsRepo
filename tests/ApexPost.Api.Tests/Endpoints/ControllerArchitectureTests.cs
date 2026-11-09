using System.Reflection;
using ApexPost.Api.Controllers;
using ApexPost.Application.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// Controllers stay thin: they depend on the tracking service only (no DbContext, repository or query service), use
/// [ApiController] with attribute routes, and constrain every tracking-number route parameter.
/// </summary>
public sealed class ControllerArchitectureTests
{
    private static readonly Type[] Controllers = [.. typeof(ShipmentsController).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)];

    [Fact]
    public void Every_controller_is_an_api_controller_with_an_attribute_route()
    {
        Assert.Equal(2, Controllers.Length);
        Assert.All(Controllers, controller =>
        {
            Assert.NotNull(controller.GetCustomAttribute<ApiControllerAttribute>());
            Assert.NotNull(controller.GetCustomAttribute<RouteAttribute>());
        });
    }

    [Fact]
    public void Controllers_depend_on_the_tracking_service_only()
    {
        Assert.All(Controllers, controller =>
            Assert.All(controller.GetConstructors().SelectMany(constructor => constructor.GetParameters()), parameter =>
                Assert.Equal(typeof(ShipmentTrackingService), parameter.ParameterType)));
    }

    [Fact]
    public void Every_tracking_number_in_a_route_has_the_constraint()
    {
        var templates = Controllers
            .SelectMany(controller => new[] { controller.GetCustomAttribute<RouteAttribute>()!.Template }
                .Concat(controller.GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()).Select(attribute => attribute.Template ?? string.Empty)))
            .Where(template => template.Contains("{trackingNumber", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(templates);
        Assert.All(templates, template => Assert.Contains("{trackingNumber:trackingNumber}", template));
    }

    [Fact]
    public void The_api_has_the_shipment_and_scan_history_endpoints()
    {
        var routes = Controllers
            .SelectMany(controller => controller.GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Select(attribute => $"{attribute.HttpMethods.Single()} {Combine(controller.GetCustomAttribute<RouteAttribute>()!.Template, attribute.Template)}")))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "DELETE api/shipments/{trackingNumber:trackingNumber}",
            "GET api/shipments",
            "GET api/shipments/{trackingNumber:trackingNumber}",
            "GET api/shipments/{trackingNumber:trackingNumber}/scans",
            "PATCH api/shipments/{trackingNumber:trackingNumber}/status",
            "POST api/shipments",
            "POST api/shipments/{trackingNumber:trackingNumber}/scans",
            "PUT api/shipments/{trackingNumber:trackingNumber}",
        ], routes);
    }

    private static string Combine(string prefix, string? template) => string.IsNullOrEmpty(template) ? prefix : $"{prefix}/{template}";
}
