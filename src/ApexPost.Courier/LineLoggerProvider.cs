using Microsoft.Extensions.Logging;

namespace ApexPost.Courier;

/// <summary>
/// Writes each log entry as one line, straight away, to the same writer as the screens. A queued console logger
/// would print listener and sync lines some time after the menu, which makes the app's output hard to follow.
/// The level filter comes from the Logging section of the configuration.
/// </summary>
internal sealed class LineLoggerProvider(TextWriter output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LineLogger(output, categoryName[(categoryName.LastIndexOf('.') + 1)..]);

    public void Dispose()
    {
    }

    private sealed class LineLogger(TextWriter output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var detail = exception is null ? string.Empty : $" ({exception.Message})";
            output.WriteLine($"  [{Level(logLevel)} {category}] {formatter(state, exception)}{detail}");
        }

        private static string Level(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "dbug",
            LogLevel.Warning => "warn",
            LogLevel.Error or LogLevel.Critical => "fail",
            _ => "info",
        };
    }
}
