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
        if (context.ModelState.IsValid || context.ActionDescriptor.EndpointMetadata.OfType<HandlesInvalidModelAttribute>().Any())
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

        // The view is shown again only with the posted form model. An action that only takes an ID (a label, an edit
        // page) has no model to show when the ID fails binding, so it gets the 400 instead of a view rendered without one.
        var formModel = context.ActionArguments.Values.FirstOrDefault(value => value is not CancellationToken);
        if (context.Controller is Controller controller && formModel is not null and not string)
        {
            controller.ViewData.Model = formModel;
            context.Result = new ViewResult { ViewData = controller.ViewData, TempData = controller.TempData };
            return;
        }

        context.Result = new BadRequestObjectResult(details);
        return;
    }
}
