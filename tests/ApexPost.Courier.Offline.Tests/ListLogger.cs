using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>Keeps every formatted log message so a test can check what was logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyList<string> Messages => [.. _messages];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _messages.Enqueue(formatter(state, exception));
}
