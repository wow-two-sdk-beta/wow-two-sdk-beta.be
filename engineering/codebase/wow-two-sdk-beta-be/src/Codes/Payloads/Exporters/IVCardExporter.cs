using WoW.Two.Sdk.Beta.Be.Codes.Payloads.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Exporters;

/// <summary>Defines export of vCard 3.0 contact documents.</summary>
public interface IVCardExporter
{
    /// <summary>Exports supplied data under the documented content-line contract.</summary>
    string Export(ContactCardModel model);
}
