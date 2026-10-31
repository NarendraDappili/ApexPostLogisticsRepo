using System.Text.Json;
using ApexPost.Application.Abstractions;
using ApexPost.Application.BranchManagement;
using ApexPost.Application.Transactions;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Web.IntegrationTests;

/// <summary>Runs the real middleware against exceptions raised the way the application raises them.</summary>
public sealed class ProblemDetailsMappingTests
{
    [Fact]
    public async Task A_concurrency_conflict_or_duplicate_key_is_409()
    {
        var conflict = await InvokeAsync(() => throw new ConcurrencyConflictException("Someone else changed this data first."));
        var duplicate = await InvokeAsync(() => throw new DuplicateKeyException("A record with the same identifier already exists."));

        Assert.Equal((409, "Someone else changed this data first."), (conflict.Status, conflict.Detail));
        Assert.Equal((409, "A record with the same identifier already exists."), (duplicate.Status, duplicate.Detail));
    }

    [Fact]
    public async Task A_missing_record_is_404()
    {
        var problem = await InvokeAsync(() => throw new CounterTransactionNotFoundException("The counter transaction was not found."));

        Assert.Equal((404, "The requested record was not found.", "The counter transaction was not found."), (problem.Status, problem.Title, problem.Detail));
    }

    [Fact]
    public async Task A_rule_broken_inside_the_domain_is_400_with_the_business_message()
    {
        var problem = await InvokeAsync(() => new BranchInventoryItem(new BranchCode("BR-1"), new ProductId("P-1"), 1).RemoveStock(2));

        Assert.Equal((400, "Stock cannot be reduced below zero."), (problem.Status, problem.Detail));
    }

    [Fact]
    public async Task A_guard_clause_in_business_code_is_400_without_the_parameter_name()
    {
        var domain = await InvokeAsync(() => _ = new Money(-1m));
        var application = await InvokeAsync(() => RequestValidator.Validate(new BranchDetailsRequest(" ", "Name", "Region", "Hours")));

        Assert.Equal(400, domain.Status);
        Assert.DoesNotContain("Parameter", domain.Detail, StringComparison.Ordinal);
        Assert.Equal(400, application.Status);
        Assert.Contains("BranchCode", application.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_exception_types_from_framework_code_stay_500_without_details()
    {
        var problem = await InvokeAsync(() => _ = new List<int>().First());

        Assert.Equal((500, null), (problem.Status, problem.Detail));
        Assert.Equal("safe-correlation", problem.CorrelationId);
    }

    private static async Task<(int Status, string? Title, string? Detail, string? CorrelationId, string? ContentType)> InvokeAsync(Action failure)
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.ItemName] = "safe-correlation";
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var middleware = new ProblemDetailsExceptionMiddleware(_ => { failure(); return Task.CompletedTask; }, NullLogger<ProblemDetailsExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        var root = json.RootElement;
        Assert.Equal(context.Response.StatusCode, root.GetProperty("status").GetInt32());
        Assert.Equal("application/problem+json", context.Response.ContentType);
        return (context.Response.StatusCode,
            root.GetProperty("title").GetString(),
            root.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
            root.GetProperty("correlationId").GetString(),
            context.Response.ContentType);
    }
}
