using System.ComponentModel.DataAnnotations;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Api.Validation;

/// <summary>
/// Custom rule: a phone number in a format a courier can dial. It asks the domain (<see cref="Recipient.IsValidPhone"/>),
/// so the request check and the domain rule can never disagree: 7 to 15 digits (the E.164 limit), with an optional
/// leading +, spaces, hyphens and brackets. The built-in [Phone] accepts far more, such as "ext" suffixes and letters.
/// A missing or blank value is left to [Required].
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class PhoneNumberAttribute() : ValidationAttribute("{0} must have 7 to 15 digits and may contain only a leading +, spaces, hyphens and brackets.")
{
    /// <inheritdoc/>
    public override bool IsValid(object? value) => value is null || (value is string phone && (string.IsNullOrWhiteSpace(phone) || Recipient.IsValidPhone(phone)));
}
