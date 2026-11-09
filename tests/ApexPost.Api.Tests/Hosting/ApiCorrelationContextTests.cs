using System.Diagnostics;
using ApexPost.Api.Composition;
using Microsoft.AspNetCore.Http;

namespace ApexPost.Api.Tests.Hosting;

public sealed class ApiCorrelationContextTests
{
    [Fact]
    public void Uses_the_w3c_trace_id_of_the_current_request()
    {
        using var activity = new Activity("api-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new ApiCorrelationContext(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Equal(activity.TraceId.ToString(), context.CorrelationId);
        Assert.True(context.CorrelationId!.Length <= 64, "The DataChanges.CorrelationId column holds 64 characters.");
    }

    [Fact]
    public void Falls_back_to_the_request_trace_identifier_without_an_activity()
    {
        Activity.Current = null;
        var httpContext = new DefaultHttpContext { TraceIdentifier = "0HN-TEST:00000001" };
        var context = new ApiCorrelationContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Equal("0HN-TEST:00000001", context.CorrelationId);
    }

    [Fact]
    public void Has_no_correlation_id_outside_a_request()
    {
        var context = new ApiCorrelationContext(new HttpContextAccessor());

        Assert.Null(context.CorrelationId);
    }
}
