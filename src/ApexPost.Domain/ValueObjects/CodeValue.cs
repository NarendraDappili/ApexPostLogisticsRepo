namespace ApexPost.Domain.ValueObjects;

internal static class CodeValue
{
    internal static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value.Trim().ToUpperInvariant();
    }
}
