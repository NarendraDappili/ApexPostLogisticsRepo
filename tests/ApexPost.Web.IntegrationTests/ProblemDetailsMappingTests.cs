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

    // The classification rule itself, with this test assembly standing in for Domain and Application: code here is
    // "business code", so each case is thrown from a business frame, exactly as it would be inside a domain method.
    [Fact]
    public void Guard_helpers_called_by_business_code_count_as_business_code()
    {
        Assert.True(ThrownByThisAssembly(() => ArgumentException.ThrowIfNullOrWhiteSpace(" ")));
        Assert.True(ThrownByThisAssembly(() => ArgumentNullException.ThrowIfNull((object?)null)));
        Assert.True(ThrownByThisAssembly(() => ArgumentOutOfRangeException.ThrowIfNegative(-1m)));
        Assert.True(ThrownByThisAssembly(() => throw new InvalidOperationException("Only an open or paid transaction can be voided.")));
    }

    [Fact]
    public void A_bug_inside_a_dotnet_method_called_by_business_code_is_not_a_broken_rule()
    {
        // Same exception types as the domain's rules, but thrown by List, Dictionary, string, Nullable and an enumerator:
        // these are server faults, so they stay 500 and their framework text is never shown to the user.
        Assert.False(ThrownByThisAssembly(() => _ = new List<int>()[3]));
        Assert.False(ThrownByThisAssembly(() => new Dictionary<int, int> { [1] = 1 }.Add(1, 1)));
        Assert.False(ThrownByThisAssembly(() => _ = "abc".Substring(5)));
        Assert.False(ThrownByThisAssembly(() => _ = ((int?)null)!.Value));
        Assert.False(ThrownByThisAssembly(() => { var items = new List<int> { 1 }; foreach (var item in items) items.Add(item); }));
    }

    private static bool ThrownByThisAssembly(Action failure)
    {
        try { failure(); }
        catch (Exception exception) { return ExceptionProblemMapping.IsThrownBy(exception, [typeof(ProblemDetailsMappingTests).Assembly]); }
        throw new InvalidOperationException("The action was expected to throw.");
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
