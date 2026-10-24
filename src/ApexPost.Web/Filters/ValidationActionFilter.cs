using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApexPost.Web.Filters;

/// <summary>Provides one model-validation response policy for MVC actions.</summary>
public sealed class ValidationActionFilter : IAsyncActionFilter, IOrderedFilter
{
    public int Order => -1000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.ModelState.IsValid)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var details = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        };

        if (context.ActionDescriptor.EndpointMetadata.OfType<InvalidModelResponseAttribute>().Any() || context.Controller is ControllerBase { } controllerBase && context.Controller.GetType().GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).Length > 0)
        {
            context.Result = new BadRequestObjectResult(details);
            return;
        }

        if (context.Controller is Controller controller)
        {
            controller.ViewData.Model = context.ActionArguments.Values.FirstOrDefault(value => value is not CancellationToken);
            context.Result = new ViewResult { ViewData = controller.ViewData, TempData = controller.TempData };
            return;
        }

        context.Result = new BadRequestObjectResult(details);
        return;
    }
}
