using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>
/// Page navigation (GET/HEAD) is redirected to the Access Denied page; a denied state-changing request such as a
/// void or refund POST receives a plain 403, because there is no page to return to and the status is the answer.
/// </summary>
public static class AccessDeniedResponder
{
    public static Task RespondAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.Redirect(context.RedirectUri);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        return Task.CompletedTask;
    }
}
