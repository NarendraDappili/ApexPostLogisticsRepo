using System.Collections.Concurrent;
using System.Reflection;

namespace ApexPost.Application.Validation;

/// <summary>A declarative rule on a request property, evaluated by <see cref="RequestValidator"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public abstract class RequestRuleAttribute : Attribute
{
    /// <summary>Returns an error message, or null when the value satisfies the rule.</summary>
    public abstract string? Validate(string propertyName, object? value);
}

/// <summary>Requires non-blank text no longer than <see cref="MaxLength"/> characters after trimming.</summary>
public sealed class NotBlankAttribute(int maxLength = int.MaxValue) : RequestRuleAttribute
{
    public int MaxLength { get; } = maxLength;

    public override string? Validate(string propertyName, object? value) => value switch
    {
        null => $"{propertyName} is required.",
        string text when string.IsNullOrWhiteSpace(text) => $"{propertyName} is required.",
        string text when text.Trim().Length > MaxLength => $"{propertyName} must be {MaxLength} characters or fewer.",
        _ => null
    };
}

public sealed class RequestValidationException(IReadOnlyList<string> errors) : ArgumentException(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Validates application requests from their <see cref="RequestRuleAttribute"/> metadata. Reflection runs once per
/// request type; the property/rule list is cached so every later request only reads property values.
/// </summary>
public static class RequestValidator
{
    private static readonly ConcurrentDictionary<Type, PropertyRules[]> Metadata = new();

    public static void Validate<TRequest>(TRequest request) where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);
        List<string>? errors = null;
        foreach (var property in Describe(request.GetType()))
        {
            var value = property.Property.GetValue(request);
            foreach (var rule in property.Rules)
            {
                if (rule.Validate(property.Property.Name, value) is { } error) (errors ??= []).Add(error);
            }
        }

        if (errors is not null) throw new RequestValidationException(errors);
    }

    /// <summary>Returns the cached rule metadata for a type, building it on first use.</summary>
    public static IReadOnlyList<PropertyRules> Describe(Type requestType) =>
        Metadata.GetOrAdd(requestType, static type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => new PropertyRules(property, [.. property.GetCustomAttributes<RequestRuleAttribute>(inherit: true)]))
            .Where(property => property.Rules.Count > 0)
            .ToArray());

    public sealed record PropertyRules(PropertyInfo Property, IReadOnlyList<RequestRuleAttribute> Rules);
}
