using System.Reflection;
using ApexPost.Api.Controllers.V1;
using ApexPost.Application.Tracking;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// Controllers stay thin: they depend on the tracking service only (no DbContext, repository or query service), use
/// [ApiController] with attribute routes, and constrain every tracking-number route parameter. C4: each controller
/// belongs to exactly one API version; v1 is deprecated; v2 has the same actions as v1 and differs only in its
/// response DTOs.
/// </summary>
public sealed class ControllerArchitectureTests
{
    private static readonly Type[] Controllers = [.. typeof(ShipmentsController).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)];

    [Fact]
    public void Every_controller_is_an_api_controller_with_an_attribute_route()
    {
        Assert.Equal(5, Controllers.Length);
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
    public void Each_controller_serves_one_version_v1_deprecated_and_v2_current()
    {
        Assert.All(Controllers, controller =>
        {
            var version = Assert.Single(controller.GetCustomAttributes<ApiVersionAttribute>());
            var expected = controller.Namespace!.EndsWith(".V1", StringComparison.Ordinal) ? (1, true) : (2, false);
            Assert.Equal(expected, (version.Versions.Single().MajorVersion!.Value, version.Deprecated));
            Assert.StartsWith(controller == typeof(Controllers.V2.PartnerShipmentsController) ? "api/v{version:apiVersion}/partner/shipments" : "api/v{version:apiVersion}/shipments", controller.GetCustomAttribute<RouteAttribute>()!.Template, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Both_versions_have_the_shipment_and_scan_endpoints()
    {
        string[] expected =
        [
            "DELETE api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber} DeleteShipment",
            "GET api/v{version:apiVersion}/shipments ListShipments",
            "GET api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber} GetShipment",
            "GET api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber}/scans GetScanHistory",
            "PATCH api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber}/status UpdateShipmentStatus",
            "POST api/v{version:apiVersion}/shipments CreateShipment",
            "POST api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber}/scans UploadScans",
            "PUT api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber} UpdateShipment",
        ];

        Assert.Equal(expected, Routes(".V1"));
        Assert.Equal(expected, Routes(".V2"));
    }

    [Fact]
    public void Both_versions_take_the_same_requests()
    {
        Assert.Equal(Parameters(".V1"), Parameters(".V2"));
    }

    [Fact]
    public void V2_returns_only_v2_or_shared_response_dtos_never_v1_ones()
    {
        var v1Responses = new[] { typeof(Contracts.ShipmentResponse), typeof(Contracts.ScanResponse), typeof(Contracts.ScanUploadResponse) };
        var v2Types = Actions(".V2")
            .SelectMany(method => method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(attribute => attribute.Type))
            .SelectMany(Flatten)
            .Distinct()
            .ToList();

        Assert.Contains(typeof(Contracts.V2.ShipmentResponse), v2Types);
        Assert.Contains(typeof(Contracts.V2.ScanResponse), v2Types);
        Assert.Contains(typeof(Contracts.V2.ScanUploadResponse), v2Types);
        Assert.DoesNotContain(v2Types, v1Responses.Contains);
    }

    [Fact]
    public void The_partner_route_is_read_only_and_in_v2_only()
    {
        var partner = typeof(Controllers.V2.PartnerShipmentsController);
        var action = Assert.Single(partner.GetMethods(), method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

        Assert.Equal(["GET"], action.GetCustomAttribute<HttpMethodAttribute>()!.HttpMethods);
        Assert.Equal(2, partner.GetCustomAttribute<ApiVersionAttribute>()!.Versions.Single().MajorVersion);
        Assert.DoesNotContain(Controllers, controller => controller.Namespace!.EndsWith(".V1", StringComparison.Ordinal) && controller.Name.StartsWith("Partner", StringComparison.Ordinal));
    }

    // The shipment and scan controllers, which both versions have. The partner route (D3) is an addition to v2 only.
    private static IEnumerable<MethodInfo> Actions(string versionNamespace) =>
        Controllers.Where(controller => controller.Namespace!.EndsWith(versionNamespace, StringComparison.Ordinal) && controller != typeof(Controllers.V2.PartnerShipmentsController))
            .SelectMany(controller => controller.GetMethods().Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any()));

    private static List<string> Routes(string versionNamespace) =>
        [.. Actions(versionNamespace)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>().Select(attribute =>
                $"{attribute.HttpMethods.Single()} {Combine(method.DeclaringType!.GetCustomAttribute<RouteAttribute>()!.Template, attribute.Template)} {attribute.Name}"))
            .Order(StringComparer.Ordinal)];

    private static List<string> Parameters(string versionNamespace) =>
        [.. Actions(versionNamespace)
            .Select(method => $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})")
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<Type> Flatten(Type type) => type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type) : [type];

    private static string Combine(string prefix, string? template) => string.IsNullOrEmpty(template) ? prefix : $"{prefix}/{template}";
}
