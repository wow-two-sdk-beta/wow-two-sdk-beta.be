using WoW.Two.Sdk.Beta.Be.Codes.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Codes;

/// <summary>Defines rendering a code (QR or barcode) to SVG or PNG — the single entry point consumed by services.</summary>
public interface ICodeRenderer
{
    /// <summary>Renders the requested code image.</summary>
    /// <param name="request">The render request describing the payload, symbology, format, and style.</param>
    Result<RenderedCode> Render(CodeRenderRequest request);
}
