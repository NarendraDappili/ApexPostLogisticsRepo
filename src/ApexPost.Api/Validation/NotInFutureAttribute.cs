using System.ComponentModel.DataAnnotations;

namespace ApexPost.Api.Validation;

/// <summary>
/// Custom rule: a scan time cannot be in the future. A courier records a scan when it happens, so a later time means
/// the device clock is wrong. A few minutes of drift are allowed (the same 5 minutes as the courier's offline fake
/// server), because phone clocks are never exactly right. "Now" comes from the registered <see cref="TimeProvider"/>,
/// so tests can fix it. A missing value is left to [Required].
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NotInFutureAttribute : ValidationAttribute
{
    /// <summary>How far ahead of the server clock a scan time may be: 5 minutes.</summary>
    public const int DefaultAllowedClockSkewMinutes = 5;

    /// <summary>The rule with the default 5 minutes of allowed clock drift.</summary>
    public NotInFutureAttribute()
        : base("{0} cannot be in the future; it is more than {1} minutes ahead of the server clock.")
    {
    }

    /// <summary>How many minutes ahead of the server clock a time may be and still pass.</summary>
    public int AllowedClockSkewMinutes { get; init; } = DefaultAllowedClockSkewMinutes;

    /// <inheritdoc/>
    public override string FormatErrorMessage(string name) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name, AllowedClockSkewMinutes);

    /// <inheritdoc/>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var time = value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime),
            _ => (DateTimeOffset?)null,
        };
        if (time is null) return ValidationResult.Success;

        var clock = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var latestAllowed = clock.GetUtcNow().AddMinutes(AllowedClockSkewMinutes);
        return time <= latestAllowed
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), validationContext.MemberName is { } member ? [member] : null);
    }
}
