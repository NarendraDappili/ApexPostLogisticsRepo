namespace ApexPost.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;
            response.Headers.TryAdd("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'");
            response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            response.Headers.TryAdd("X-Frame-Options", "DENY");
            response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            return Task.CompletedTask;
        }, context);
        await next(context).ConfigureAwait(false);
    }
}
