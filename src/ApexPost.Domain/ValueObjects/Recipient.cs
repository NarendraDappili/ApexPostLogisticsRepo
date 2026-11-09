using System.Text.RegularExpressions;

namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Who a tracked shipment is delivered to. Every part is required and trimmed. The phone number may contain spaces,
/// hyphens, brackets and a leading +, and must have 7 to 15 digits (the E.164 maximum).
/// </summary>
public sealed partial record Recipient
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int AddressMaxLength = 200;
    public const int CityMaxLength = 80;

    public Recipient(string name, string phone, string address, string city)
    {
        Name = Required(name, NameMaxLength, nameof(name));
        Phone = Required(phone, PhoneMaxLength, nameof(phone));
        if (!IsValidPhone(Phone)) throw new ArgumentException("A phone number must have 7 to 15 digits and may contain only +, spaces, hyphens and brackets.", nameof(phone));
        Address = Required(address, AddressMaxLength, nameof(address));
        City = Required(city, CityMaxLength, nameof(city));
    }

    public string Name { get; }

    public string Phone { get; }

    public string Address { get; }

    public string City { get; }

    /// <summary>The phone rule, shared with request validation so both give the same answer.</summary>
    public static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var trimmed = phone.Trim();
        if (trimmed.Length > PhoneMaxLength || !PhoneFormat().IsMatch(trimmed)) return false;
        var digits = trimmed.Count(char.IsAsciiDigit);
        return digits is >= 7 and <= 15;
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength) throw new ArgumentException($"Must be at most {maximumLength} characters.", parameterName);
        return trimmed;
    }

    // A leading + only, then digits and the usual separators; the digit count is checked separately.
    [GeneratedRegex(@"^\+?[0-9 ()\-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneFormat();
}
