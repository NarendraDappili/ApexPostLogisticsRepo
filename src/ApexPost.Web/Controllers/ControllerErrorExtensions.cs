using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

// Forms show only failures that ExceptionProblemMapping recognises: ApexPost conflicts, missing records and broken
// business rules, with the same safe message Problem Details would return. An ArgumentException or
// InvalidOperationException from EF Core or the framework is not caught, so it becomes a 500 without details.
internal static class ControllerErrorExtensions
{
    /// <summary>
    /// A form redisplayed because someone else changed the data first (or a unique value is taken) answers 409,
    /// so the conflict is visible to clients and tests, not only in the page text.
    /// </summary>
    public static void UseConflictStatus(this ControllerBase controller, ProblemMapping problem)
    {
        if (problem.Status == StatusCodes.Status409Conflict) controller.Response.StatusCode = StatusCodes.Status409Conflict;
    }
}
