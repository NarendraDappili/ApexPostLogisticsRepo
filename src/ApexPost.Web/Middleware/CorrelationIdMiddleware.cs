using System.Diagnostics;

namespace ApexPost.Web.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemName = "ApexPost.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var candidate = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(candidate) ? candidate! : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.Items[ItemName] = correlationId;
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            httpContext.Response.Headers[HeaderName] = (string)httpContext.Items[ItemName]!;
            return Task.CompletedTask;
        }, context);
        await next(context).ConfigureAwait(false);
    }

    public static string Get(HttpContext context) => context.Items.TryGetValue(ItemName, out var value) ? value as string ?? context.TraceIdentifier : context.TraceIdentifier;

    private static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
