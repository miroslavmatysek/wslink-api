using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WsLink.Business.Tests.Fakes;

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Collects all log entries (including Debug), so tests can check what and how was logged.
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> entries = new();

    public IReadOnlyList<LogEntry> Entries => entries.ToList();

    public IReadOnlyList<LogEntry> AtLevel(LogLevel level) => Entries.Where(e => e.Level == level).ToList();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
}
