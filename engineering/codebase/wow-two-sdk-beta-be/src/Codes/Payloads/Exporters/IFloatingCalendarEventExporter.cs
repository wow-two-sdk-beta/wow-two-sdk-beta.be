using WoW.Two.Sdk.Beta.Be.Codes.Payloads.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Exporters;

/// <summary>Defines export of floating VEVENT fragments.</summary>
public interface IFloatingCalendarEventExporter
{
    /// <summary>Exports supplied data under the documented content-line contract.</summary>
    Result<string> Export(FloatingCalendarEventModel model);
}
