using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Offline.Harness;

/// <summary>
/// Writes each log entry as one line, straight away, to the same writer as the menu. A queued console logger would
/// print listener lines some time after the menu, which makes the harness output hard to follow.
/// </summary>
internal sealed class LineLogger<T>(TextWriter output, LogLevel minimumLevel = LogLevel.Information) : ILogger<T>
{
    private static readonly string Category = typeof(T).Name;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var detail = exception is null ? string.Empty : $" ({exception.Message})";
        output.WriteLine($"  [{Level(logLevel)} {Category}] {formatter(state, exception)}{detail}");
    }

    private static string Level(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Warning => "warn",
        LogLevel.Error or LogLevel.Critical => "fail",
        _ => "info",
    };
}
