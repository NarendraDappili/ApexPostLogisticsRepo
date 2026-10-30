using ApexPost.Application.Validation;

namespace ApexPost.Application.Tests.Validation;

public sealed class RequestValidatorTests
{
    private sealed record SampleRequest([property: NotBlank(5)] string Code, [property: NotBlank] string? Name, string Unchecked);

    [Fact]
    public void Valid_requests_pass_and_every_violation_is_reported()
    {
        RequestValidator.Validate(new SampleRequest("AB", "Name", ""));

        var exception = Assert.Throws<RequestValidationException>(() => RequestValidator.Validate(new SampleRequest("TOO-LONG", null, "")));
        Assert.Equal(["Code must be 5 characters or fewer.", "Name is required."], exception.Errors);
    }

    [Fact]
    public void Reflection_metadata_is_built_once_per_type_and_reused()
    {
        var first = RequestValidator.Describe(typeof(SampleRequest));
        var second = RequestValidator.Describe(typeof(SampleRequest));

        Assert.Same(first, second);
        Assert.Equal(["Code", "Name"], first.Select(rule => rule.Property.Name));
    }
}
