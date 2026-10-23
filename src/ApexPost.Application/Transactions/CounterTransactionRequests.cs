using ApexPost.Domain.Enums;

namespace ApexPost.Application.Transactions;

public sealed record CounterCheckoutRequest(
    string TransactionId,
    string BranchCode,
    string CounterCode,
    string ClerkId,
    PaymentMethod PaymentMethod,
    IReadOnlyCollection<RetailBasketLineRequest> RetailLines,
    IReadOnlyCollection<ServiceBasketLineRequest> ServiceLines,
    IReadOnlyCollection<string> MoneyOrderIds);

public sealed record RetailBasketLineRequest(string ProductId, int Quantity);

/// <summary>A service price must originate from its owning service workflow; checkout does not recalculate it.</summary>
public sealed record ServiceBasketLineRequest(string Description, decimal UnitPrice, int Quantity = 1);

public sealed record ManagerTransactionActionRequest(string ManagerId, string Reason);
