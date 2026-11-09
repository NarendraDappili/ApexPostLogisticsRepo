namespace ApexPost.Api.Contracts;

/// <summary>How a value is written in a response, the same in every API version.</summary>
internal static class ResponseValues
{
    // The column keeps three decimals, so a stored 69.5 reads back as 69.500. Dropping trailing zeros makes a create
    // and a later read of the same shipment return the same JSON.
    public static decimal Weight(decimal kilograms) => kilograms / 1.000000000000000000000000000000000m;
}
