namespace ApexPost.Web.Security;

public static class AuthorizationPolicies
{
    public const string BranchManagerOnly = nameof(BranchManagerOnly);
    public const string SameBranchOnly = nameof(SameBranchOnly);
}
