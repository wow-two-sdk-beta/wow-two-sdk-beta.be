using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;

/// <summary>Extends the person operations domain with its log messages.</summary>
internal static partial class PersonOperationLogExtensions
{
    [LoggerMessage(EventId = 8111, Level = LogLevel.Error, Message = "Person operation {Operation} was cancelled ({Reason}), and a callback on its token threw")]
    public static partial void PersonOperationCallbackFailed(this ILogger logger, Exception exception, string operation, string reason);
}
