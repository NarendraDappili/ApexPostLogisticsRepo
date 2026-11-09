using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace ApexPost.Api.Tests.Hosting;

/// <summary>
/// Partner API keys for the test hosts: two fictional partners with random keys made up for each test run, given to
/// the API through the same PartnerApi:Keys settings a developer puts in user-secrets.
/// </summary>
internal static class TestPartnerKeys
{
    public const string Northwind = "Northwind Freight (test)";
    public const string Contoso = "Contoso Logistics (test)";

    public static readonly string NorthwindKey = NewKey();
    public static readonly string ContosoKey = NewKey();

    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("PartnerApi:Keys:0:Partner", Northwind);
        builder.UseSetting("PartnerApi:Keys:0:Key", NorthwindKey);
        builder.UseSetting("PartnerApi:Keys:1:Partner", Contoso);
        builder.UseSetting("PartnerApi:Keys:1:Key", ContosoKey);
    }

    /// <summary>32 random bytes in Base64: the kind of key ApexPost issues (SECURITY.md).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
