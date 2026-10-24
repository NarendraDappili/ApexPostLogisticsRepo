namespace ApexPost.Web.Filters;

/// <summary>Requests a 400 validation response for an MVC endpoint without a corresponding view.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InvalidModelResponseAttribute : Attribute;
