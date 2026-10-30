using ApexPost.Domain.Enums;

namespace ApexPost.Application.Transactions;

/// <summary>
/// A mixed basket. Every price is resolved on the server: retail lines from the catalogue, shipments from the
/// configured rates and money orders from their policy-calculated fee. The request carries identifiers only.
/// </summary>
public sealed record CounterCheckoutRequest(
    string TransactionId,
    string BranchCode,
    string CounterCode,
    string ClerkId,
    PaymentMethod PaymentMethod,
    IReadOnlyCollection<RetailBasketLineRequest> RetailLines,
    IReadOnlyCollection<string> ShipmentIds,
    IReadOnlyCollection<string> MoneyOrderIds);

public sealed record RetailBasketLineRequest(string ProductId, int Quantity);

public sealed record ManagerTransactionActionRequest(string ManagerId, string Reason);
