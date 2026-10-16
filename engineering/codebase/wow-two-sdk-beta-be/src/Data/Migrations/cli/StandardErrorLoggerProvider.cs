using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

/// <summary>Writes engine log messages at or above a minimum level to stderr as one <c>✗</c> line each, without stack traces.</summary>
/// <param name="minimumLevel">The lowest level written.</param>
internal sealed class StandardErrorLoggerProvider(LogLevel minimumLevel) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new StandardErrorLogger(minimumLevel);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>Writes one formatted message per entry; the verb that rethrows prints the exception message itself.</summary>
    /// <param name="minimumLevel">The lowest level written.</param>
    private sealed class StandardErrorLogger(LogLevel minimumLevel) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimumLevel;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Console.Error.WriteLine($"✗ {formatter(state, exception)}");
        }
    }
}
