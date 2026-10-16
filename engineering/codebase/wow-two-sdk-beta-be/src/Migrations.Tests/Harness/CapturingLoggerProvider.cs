using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Backend.Beta.Migrations.Tests.Harness;

/// <summary>Captures every log entry in memory so a test can assert what the migrator reported.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>The captured entries, in write order.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLogEntry(logLevel, eventId.Id, formatter(state, exception), exception));
    }
}

/// <summary>One captured log entry.</summary>
/// <param name="Level">The entry's level.</param>
/// <param name="EventId">The entry's event id.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The attached exception, if any.</param>
public sealed record CapturedLogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);
