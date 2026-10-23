using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IShipmentRepository
{
    Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default);

    Task SaveAsync(Shipment shipment, CancellationToken cancellationToken = default);
}
