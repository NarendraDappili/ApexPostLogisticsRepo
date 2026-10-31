using ApexPost.Application.Abstractions;

namespace ApexPost.Application.Shipping;

public sealed class ShipmentNotFoundException(string message) : NotFoundException(message);

public sealed class OriginBranchNotFoundException(string message) : InvalidOperationException(message);
