namespace ApexPost.Application.Shipping;

public sealed class ShipmentNotFoundException(string message) : InvalidOperationException(message);

public sealed class OriginBranchNotFoundException(string message) : InvalidOperationException(message);
