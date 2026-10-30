using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IShipmentRepository
{
    Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    void Add(Shipment shipment);
}
