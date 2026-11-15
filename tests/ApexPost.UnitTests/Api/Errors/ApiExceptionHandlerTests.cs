using ApexPost.Api.Errors;
using ApexPost.Application.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.UnitTests.Api.Errors;

/// <summary>
/// Week 5 (S10a), risks R7 and R9. The coverage report showed that no test reaches the handler's mapping of a
/// service-level <see cref="RequestValidationException"/>. In the API, the request checks catch bad input first, so
/// this safety net only matters when a rule fires inside a service. It must still answer like [ApiController]'s
/// automatic 400: one entry per field, with an unnamed error under the empty key.
/// </summary>
public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ServiceValidationErrors_Writes400WithTheErrorsGroupedByField()
    {
        // Arrange
        var problems = new CapturingProblemDetailsService();
        var handler = new ApiExceptionHandler(problems, Environment("Production"), NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        var errors = new RequestValidationException(["weightKg: The weight must be between 0.1 and 70 kg.", "weightKg: It must be a number.", "The request is empty."]);

        // Act
        var handled = await handler.TryHandleAsync(context, errors, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(problems.Written?.ProblemDetails);
        Assert.Equal(["The weight must be between 0.1 and 70 kg.", "It must be a number."], problem.Errors["weightKg"]);
        Assert.Equal(["The request is empty."], problem.Errors[string.Empty]);
        Assert.Same(errors, problems.Written!.Exception);
    }

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(host => host.EnvironmentName).Returns(name);
        return environment.Object;
    }

    /// <summary>Keeps what the handler asked to write, instead of writing a response body.</summary>
    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }
}
