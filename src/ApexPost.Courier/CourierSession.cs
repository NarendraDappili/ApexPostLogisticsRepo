namespace ApexPost.Courier;

/// <summary>Who is signed in on this device. Scans are recorded under this courier ID.</summary>
internal sealed class CourierSession
{
    public string? CourierId { get; private set; }

    public bool IsSignedIn => CourierId is not null;

    public void SignIn(string courierId) => CourierId = courierId;

    public void SignOut() => CourierId = null;
}
