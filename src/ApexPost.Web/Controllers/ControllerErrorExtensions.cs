using ApexPost.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

internal static class ControllerErrorExtensions
{
    /// <summary>
    /// A form redisplayed because someone else changed the data first (or a unique value is taken) answers 409,
    /// so the conflict is visible to clients and tests, not only in the page text.
    /// </summary>
    public static void UseConflictStatus(this ControllerBase controller, Exception exception)
    {
        if (exception is ConflictException) controller.Response.StatusCode = StatusCodes.Status409Conflict;
    }
}
