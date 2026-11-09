namespace ApexPost.Api.Security;

/// <summary>The roles and the claim the API's bearer tokens carry (SECURITY.md).</summary>
internal static class ApiRoles
{
    /// <summary>A courier: scans parcels and moves them along the status chain.</summary>
    public const string Courier = "Courier";

    /// <summary>The back office: manages shipments, including deleting them.</summary>
    public const string Admin = "Admin";

    /// <summary>Every role a token may carry.</summary>
    public static readonly IReadOnlyList<string> All = [Courier, Admin];

    /// <summary>The claim naming the courier a token belongs to; scans may be uploaded only under this ID.</summary>
    public const string CourierIdClaim = "courierId";
}
