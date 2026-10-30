namespace ApexPost.Application.Abstractions;

// Error categories that callers translate into responses (404 and 409). They derive from
// InvalidOperationException so existing handlers that catch business-rule failures keep working.

/// <summary>A requested aggregate does not exist.</summary>
public abstract class NotFoundException(string message) : InvalidOperationException(message);

/// <summary>The request conflicts with the current stored state.</summary>
public abstract class ConflictException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);

/// <summary>Another user changed the same data first, for example two clerks selling the last unit of a product.</summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null) : ConflictException(message, innerException);

/// <summary>A unique business key (branch code, SKU, staff email, transaction ID) is already in use.</summary>
public sealed class DuplicateKeyException(string message, Exception? innerException = null) : ConflictException(message, innerException);
