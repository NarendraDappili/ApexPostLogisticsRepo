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
    public const int DefaultAllowedClockSkewMinutes = 5;

    public NotInFutureAttribute()
        : base("{0} cannot be in the future; it is more than {1} minutes ahead of the server clock.")
    {
    }

    public int AllowedClockSkewMinutes { get; init; } = DefaultAllowedClockSkewMinutes;

    public override string FormatErrorMessage(string name) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name, AllowedClockSkewMinutes);

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
