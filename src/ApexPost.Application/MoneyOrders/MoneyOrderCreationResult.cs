using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

public sealed record MoneyOrderCreationResult(MoneyOrder MoneyOrder, Money Total, bool RequiresIdentityVerification);
