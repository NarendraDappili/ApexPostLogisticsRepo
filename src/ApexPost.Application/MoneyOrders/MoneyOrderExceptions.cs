namespace ApexPost.Application.MoneyOrders;

public sealed class MoneyOrderBranchNotFoundException(string message) : InvalidOperationException(message);

public sealed class MoneyOrderNotFoundException(string message) : InvalidOperationException(message);

public sealed class IdentityVerificationRequiredException(string message) : InvalidOperationException(message);
