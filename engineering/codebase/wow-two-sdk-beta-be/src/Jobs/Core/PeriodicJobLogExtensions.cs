using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>Extends the periodic jobs domain with its log messages.</summary>
internal static partial class PeriodicJobLogExtensions
{
    [LoggerMessage(EventId = 8101, Level = LogLevel.Information, Message = "Periodic job {Job} is off: its interval is zero or less")]
    public static partial void PeriodicJobOff(this ILogger logger, string job);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Error, Message = "Periodic job {Job} failed a pass; the next pass runs on schedule")]
    public static partial void PeriodicJobFailed(this ILogger logger, Exception exception, string job);

    [LoggerMessage(EventId = 8103, Level = LogLevel.Error, Message = "Periodic job {Job} failed a pass ({FailureType}); the next pass runs on schedule")]
    public static partial void PeriodicJobFailedQuietly(this ILogger logger, string job, string failureType);

    [LoggerMessage(EventId = 8104, Level = LogLevel.Warning, Message = "Periodic job {Job} ran past its {Timeout} pass timeout; the next pass runs on schedule")]
    public static partial void PeriodicJobTimedOut(this ILogger logger, string job, TimeSpan timeout);
}
