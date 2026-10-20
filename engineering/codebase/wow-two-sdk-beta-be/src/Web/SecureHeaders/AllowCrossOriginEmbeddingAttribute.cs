namespace WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;

/// <summary>
/// Marks an endpoint whose responses other origins may embed: <c>UseOwaspSecureHeaders</c> answers it with
/// <c>Cross-Origin-Resource-Policy: cross-origin</c> instead of <c>same-origin</c>. Reach for it on media, images or
/// downloads that a native app's web view or another site loads in place; every other endpoint keeps the preset.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class AllowCrossOriginEmbeddingAttribute : Attribute;
