namespace ApexPost.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;
            // No 'unsafe-inline': the views have no inline scripts, event handlers or style attributes, so an injected
            // <script> or onerror= cannot run even if it reached the page. A test keeps the views that way.
            response.Headers.TryAdd("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'");
            response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            response.Headers.TryAdd("X-Frame-Options", "DENY");
            response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
            response.Headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
            return Task.CompletedTask;
        }, context);
        await next(context).ConfigureAwait(false);
    }
}
