using ApexPost.Application.Auditing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>
/// Page navigation (GET/HEAD) is redirected to the Access Denied page; a denied state-changing request such as a
/// void or refund POST receives a plain 403, because there is no page to return to and the status is the answer.
/// Every refusal is recorded as an AccessDenied security event: a role or branch policy that failed, and a
/// <c>Forbid()</c> from an action's own check (another branch's label or money order), both arrive here.
/// </summary>
public static class AccessDeniedResponder
{
    public static async Task RespondAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.Redirect(context.RedirectUri);
            await SecurityEventRecorder.RecordAsync(context.HttpContext, SecurityEventType.AccessDenied, StatusCodes.Status302Found, "Sent to the Access Denied page").ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await SecurityEventRecorder.RecordAsync(context.HttpContext, SecurityEventType.AccessDenied, StatusCodes.Status403Forbidden, "Refused a state-changing request").ConfigureAwait(false);
        }
    }
}
