namespace ApexPost.Application.Auditing;

/// <summary>What happened to one row; stored by name in DataChanges.Operation.</summary>
public enum DataChangeOperation
{
    Insert,
    Update,
    Delete
}

/// <summary>Sign-in and access events; stored by name in SecurityEvents.EventType.</summary>
public enum SecurityEventType
{
    SignInSucceeded,
    SignInFailed,
    SignedOut,
    /// <summary>A signed-in staff member was refused a page or action (403).</summary>
    AccessDenied,
    /// <summary>A request was turned away by a rate limit (429).</summary>
    RateLimited,
    /// <summary>A sign-in cookie was rejected because the staff member is no longer active.</summary>
    SessionRejected
}
