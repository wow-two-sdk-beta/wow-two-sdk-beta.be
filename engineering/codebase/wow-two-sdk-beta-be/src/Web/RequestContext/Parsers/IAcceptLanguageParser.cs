using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Web.RequestContext.Models;

namespace WoW.Two.Sdk.Beta.Be.Web.RequestContext.Parsers;

/// <summary>Defines parsing of HTTP language preferences.</summary>
public interface IAcceptLanguageParser
{
    /// <summary>Parses weighted language ranges, retaining exclusions for caller-owned negotiation.</summary>
    Result<IReadOnlyList<LanguagePreferenceModel>> Parse(string? value);
}
