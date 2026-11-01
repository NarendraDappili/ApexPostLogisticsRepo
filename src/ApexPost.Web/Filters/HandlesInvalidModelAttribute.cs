namespace ApexPost.Web.Filters;

/// <summary>
/// The action answers an invalid model itself, for example by redisplaying another page with the error, so the
/// shared validation filter lets the request through.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HandlesInvalidModelAttribute : Attribute;
