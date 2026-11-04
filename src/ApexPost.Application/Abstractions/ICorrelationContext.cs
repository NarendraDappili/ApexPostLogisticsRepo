namespace ApexPost.Application.Abstractions;

/// <summary>
/// The correlation ID of the current request, so the request audit, the data changes it made and its security
/// events can be read together. Outside a request (start-up seeding) there is none.
/// </summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
