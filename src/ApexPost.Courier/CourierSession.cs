namespace ApexPost.Courier;

/// <summary>
/// Who is signed in on this device. Scans are recorded under this courier ID. When the server stops accepting the
/// courier's token (401) or it expires, <see cref="EndSignIn"/> records why, and the app takes the courier back to
/// Login at the next screen.
/// </summary>
internal sealed class CourierSession
{
    private volatile string? _signInEndedReason;

    public string? CourierId { get; private set; }

    public bool IsSignedIn => CourierId is not null;

    /// <summary>The courier who was signed in last, offered at the next Login.</summary>
    public string? LastCourierId { get; private set; }

    /// <summary>Why the sign-in ended without the courier signing out; null while it is fine.</summary>
    public string? SignInEndedReason => _signInEndedReason;

    public void SignIn(string courierId)
    {
        CourierId = courierId;
        _signInEndedReason = null;
    }

    public void SignOut()
    {
        LastCourierId = CourierId ?? LastCourierId;
        CourierId = null;
        _signInEndedReason = null;
    }

    /// <summary>Called from any thread (a background sync, a request's handler): the next screen goes to Login.</summary>
    public void EndSignIn(string reason) => _signInEndedReason ??= reason;
}
