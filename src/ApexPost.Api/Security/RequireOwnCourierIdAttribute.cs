using System.Net.Mime;
using ApexPost.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApexPost.Api.Security;

/// <summary>
/// A courier may upload scans only under their own courierId claim (SECURITY.md). The rule needs the request body,
/// which the authorization middleware has not read, so it runs as an action filter: after [ApiController] has
/// answered a malformed body with 400, before the action. Any scan under another courierId makes the whole upload a
/// 403 ProblemDetails, and nothing is stored.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequireOwnCourierIdAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var upload = context.ActionArguments.Values.OfType<ScanUploadRequest>().SingleOrDefault();
        if (upload?.Scans is null) return;

        var own = context.HttpContext.User.FindFirst(ApiRoles.CourierIdClaim)?.Value;
        // Courier IDs are compared exactly, as they are stored: "cr-1042" is not the token's "CR-1042".
        var others = upload.Scans.Select(scan => scan.CourierId).Where(id => !string.Equals(id, own, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        if (own is not null && others.Count == 0) return;

        var detail = own is null
            ? "The access token has no courierId claim, so it cannot upload scans."
            : $"A courier can upload scans only under their own courierId, {own}. This upload has scans under {string.Join(", ", others)}.";
        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(context.HttpContext, StatusCodes.Status403Forbidden, AuthenticationRegistration.ForbiddenTitle, detail: detail);
        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden, ContentTypes = { MediaTypeNames.Application.ProblemJson } };
    }
}
