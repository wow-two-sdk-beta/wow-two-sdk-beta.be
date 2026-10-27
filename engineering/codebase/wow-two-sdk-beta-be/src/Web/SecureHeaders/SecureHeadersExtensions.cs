using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Web.SecureHeaders;

/// <summary>Provides OWASP secure-headers middleware presets.</summary>
public static class SecureHeadersExtensions
{
    /// <summary>Holds the resource-policy header name.</summary>
    private const string ResourcePolicyHeader = "Cross-Origin-Resource-Policy";

    /// <summary>Uses a hardened API secure-headers preset (HSTS, nosniff, frame-deny, referrer, permissions, and cross-origin policies).</summary>
    /// <param name="app">The application request pipeline.</param>
    /// <param name="configure">Optional configuration; relaxes the opener policy for popups or switches either policy off.</param>
    public static IApplicationBuilder UseOwaspSecureHeaders(this IApplicationBuilder app, Action<SecureHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new SecureHeadersOptions();
        configure?.Invoke(options);

        // The preset writes its headers as the response starts. A callback registered ahead of it runs after it, so an
        // endpoint marked AllowCrossOriginEmbedding or SecureHeaderOverride has the last word on those headers.
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.GetEndpoint()?.Metadata is not { } metadata)
                    return Task.CompletedTask;

                if (metadata.GetMetadata<AllowCrossOriginEmbeddingAttribute>() is not null)
                    context.Response.Headers[ResourcePolicyHeader] = "cross-origin";
                foreach (var header in metadata.GetOrderedMetadata<SecureHeaderOverrideAttribute>())
                {
                    if (header.Value is null)
                        context.Response.Headers.Remove(header.Name);
                    else
                        context.Response.Headers[header.Name] = header.Value;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });

        return app.UseSecurityHeaders(policies =>
        {
            policies
                .AddDefaultSecurityHeaders()
                .AddStrictTransportSecurityMaxAgeIncludeSubDomains(maxAgeInSeconds: 60 * 60 * 24 * 365)
                .AddContentTypeOptionsNoSniff()
                .AddFrameOptionsDeny()
                .AddReferrerPolicyStrictOriginWhenCrossOrigin()
                .AddCrossOriginOpenerPolicy(b =>
                {
                    if (options.AllowOpenerPopups) b.SameOriginAllowPopups();
                    else b.SameOrigin();
                })
                .AddCrossOriginEmbedderPolicy(b => b.RequireCorp())
                .AddCrossOriginResourcePolicy(b => b.SameOrigin())
                .RemoveServerHeader();

            // AddDefaultSecurityHeaders seeds both policies, so an opt-out removes the entry rather than skipping it.
            if (!options.EnableCrossOriginOpenerPolicy)
            {
                policies.Remove("Cross-Origin-Opener-Policy");
            }

            if (!options.EnableCrossOriginEmbedderPolicy)
            {
                policies.Remove("Cross-Origin-Embedder-Policy");
            }
        });
    }
}
