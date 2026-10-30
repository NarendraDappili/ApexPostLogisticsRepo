namespace ApexPost.Application.Events;

/// <summary>Observes business events. Each observer decides which events concern it.</summary>
public interface IEventObserver
{
    Task OnEventAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default);
}

public interface IEventPublisher
{
    Task PublishAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Observer-pattern publisher: workflows raise an event once and do not know who reacts to it.
/// Observers run sequentially and their failures propagate, so a critical event is never fire-and-forget.
/// </summary>
public sealed class EventPublisher(IEnumerable<IEventObserver> observers) : IEventPublisher
{
    private readonly IEventObserver[] _observers = [.. observers];

    public async Task PublishAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);
        foreach (var observer in _observers)
        {
            await observer.OnEventAsync(applicationEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
