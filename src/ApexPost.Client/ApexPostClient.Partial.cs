namespace ApexPost.Client;

// Hand-written additions to the generated client. The generated types are partial, so these extend them without
// touching Generated/ApexPostClient.g.cs, which is replaced whenever the client is regenerated.

public partial interface IApexPostClient
{
    /// <summary>
    /// Gets one shipment, or null when no shipment has this tracking number (the API's 404). Any other error is
    /// thrown as the generated <see cref="ApexPostApiException"/>.
    /// </summary>
    Task<ShipmentResponse?> TryGetShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default);
}

public partial class ApexPostClient
{
    /// <inheritdoc/>
    public async Task<ShipmentResponse?> TryGetShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetShipmentAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        }
        catch (ApexPostApiException<ProblemDetails> notFound) when (notFound.IsNotFound)
        {
            return null;
        }
    }
}

public partial class ApexPostApiException
{
    /// <summary>True for a 404: the shipment (or the route) does not exist.</summary>
    public bool IsNotFound => StatusCode == 404;

    /// <summary>True for a 409 the client can resolve by sending the request again, such as a scan upload that met another upload.</summary>
    public bool IsConflict => StatusCode == 409;
}
