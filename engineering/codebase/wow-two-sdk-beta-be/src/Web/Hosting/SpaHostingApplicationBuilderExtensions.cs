using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace WoW.Two.Sdk.Beta.Be.Web.Hosting;

/// <summary>
/// Serves a single-page app from the same host as the API: static assets, a JSON-404 guard for unmatched API routes,
/// and a fallback to the SPA shell for client-routed paths, at the site root or under a path prefix.
/// </summary>
public static class SpaHostingApplicationBuilderExtensions
{
    /// <summary>
    /// Enables static-file serving for the SPA bundle (default document then static files). Call early in the pipeline,
    /// before authentication and endpoint mapping, so assets short-circuit and stay reachable without auth. Pair with
    /// <see cref="MapSpaFallback(WebApplication, Action{SpaHostingOptions}?)"/> after endpoints are mapped.
    /// </summary>
    /// <param name="app">The application request pipeline.</param>
    /// <param name="configure">
    /// Optional configuration; <see cref="SpaHostingOptions.PathPrefix"/>, <see cref="SpaHostingOptions.ServeDefaultFiles"/>
    /// and <see cref="SpaHostingOptions.RedirectToTrailingSlash"/> are read here.
    /// </param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="SpaHostingOptions.PathPrefix"/> holds <c>..</c>, a query or a fragment.</exception>
    public static IApplicationBuilder UseSpaHosting(this IApplicationBuilder app, Action<SpaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = BuildOptions(configure);
        var prefix = new PathString(options.PathPrefix);

        if (prefix.HasValue)
        {
            // Before default files, which would answer the bare prefix with its document; a matched real route wins.
            app.Use((context, next) =>
                context.GetEndpoint() is null && TryRedirectToSlashedPrefix(context, prefix) ? Task.CompletedTask : next(context));
        }

        if (options.ServeDefaultFiles)
        {
            // Before routing, a route folder (pricing/index.html) answers /pricing itself; the fallback covers the rest.
            var defaultFiles = new DefaultFilesOptions { RedirectToAppendTrailingSlash = options.RedirectToTrailingSlash };
            if (prefix.HasValue)
                app.UseWhen(context => context.Request.Path.StartsWithSegments(prefix), branch => branch.UseDefaultFiles(defaultFiles));
            else
                app.UseDefaultFiles(defaultFiles);
        }

        app.UseStaticFiles(StaticFiles(options));
        return app;
    }

    /// <summary>
    /// Maps the SPA fallback terminal endpoints: a JSON 404 for unmatched routes under
    /// <see cref="SpaHostingOptions.ApiPathPrefix"/> and each of <see cref="SpaHostingOptions.ReservedPathPrefixes"/>, then the route's own document for every other unmatched route —
    /// <c>pricing/index.html</c> for <c>/pricing</c> when the bundle ships one — else
    /// <see cref="SpaHostingOptions.FallbackFile"/>. With <see cref="SpaHostingOptions.PathPrefix"/> set, documents answer
    /// only under the prefix, the bare prefix answers 301 to the slashed path, and every other unmatched route answers the
    /// JSON 404. Call after the application's own endpoints are mapped so real routes win; the fallbacks allow anonymous
    /// access so the shell and 404 surface under a default-deny authorization policy.
    /// </summary>
    /// <param name="app">The built application to map endpoints onto.</param>
    /// <param name="configure">Optional configuration for the path prefix, API prefix and fallback file.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="SpaHostingOptions.PathPrefix"/> holds <c>..</c>, a query or a fragment.</exception>
    public static WebApplication MapSpaFallback(this WebApplication app, Action<SpaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = BuildOptions(configure);
        var prefix = new PathString(options.PathPrefix);

        // Terminate unmatched API and reserved routes before the SPA catch-all can return HTML; a literal prefix
        // outranks the catch-all.
        foreach (var reserved in options.ReservedPathPrefixes.Prepend(options.ApiPathPrefix).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            app.MapFallback($"{reserved.TrimEnd('/')}/{{**slug}}", NotFound)
                .AllowAnonymous();
        }

        if (prefix.HasValue)
        {
            // Outside the prefix the host owns its routes; a miss there is a real 404. File-like paths stay with the
            // static files, as under the root fallback.
            app.MapFallback("{*path:nonfile}", NotFound)
                .AllowAnonymous();
        }

        // Routing claims extensionless paths before static files run, so the fallback itself picks the document.
        var webRoot = app.Environment.WebRootFileProvider;
        var shellPath = $"{options.PathPrefix}/{options.FallbackFile.TrimStart('/')}";
        var serveDocument = ((IEndpointRouteBuilder)app).CreateApplicationBuilder()
            .Use(next => context =>
            {
                if (prefix.HasValue && TryRedirectToSlashedPrefix(context, prefix))
                    return Task.CompletedTask;

                context.Request.Path = RouteDocumentPath(webRoot, context.Request.Path) ?? shellPath;
                context.SetEndpoint(null);
                return next(context);
            })
            .UseStaticFiles(StaticFiles(options))
            .Build();
        var fallback = prefix.HasValue
            ? app.MapFallback($"{options.PathPrefix}/{{**path:nonfile}}", serveDocument)
            : app.MapFallback(serveDocument);
        fallback.AllowAnonymous();

        return app;
    }

    /// <summary>Answers an unmatched guarded route with a JSON 404 problem.</summary>
    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    /// <summary>
    /// Answers a GET or HEAD for the bare prefix (<c>/studio</c>) with 301 to the slashed path, so the bundle's relative
    /// and base-relative URLs resolve under the prefix.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <param name="prefix">The normalized SPA path prefix.</param>
    /// <returns><see langword="true"/> when the redirect was written.</returns>
    private static bool TryRedirectToSlashedPrefix(HttpContext context, PathString prefix)
    {
        var request = context.Request;
        if (!request.Path.Equals(prefix) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
            return false;

        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = $"{request.PathBase.Add(prefix).ToUriComponent()}/{request.QueryString.ToUriComponent()}";
        return true;
    }

    /// <summary>Builds the static-file options that cache hashed assets for a year and make every document revalidate.</summary>
    /// <param name="options">The SPA hosting options.</param>
    private static StaticFileOptions StaticFiles(SpaHostingOptions options)
    {
        var immutable = string.IsNullOrWhiteSpace(options.ImmutableAssetsPath)
            ? PathString.Empty
            : new PathString($"{options.PathPrefix}/{options.ImmutableAssetsPath.Trim('/')}");
        var contentTypes = new FileExtensionContentTypeProvider();
        foreach (var (extension, contentType) in options.ContentTypes)
            contentTypes.Mappings[extension.StartsWith('.') ? extension : $".{extension}"] = contentType;
        return new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                if (immutable.HasValue && context.Context.Request.Path.StartsWithSegments(immutable))
                    headers.CacheControl = "public, max-age=31536000, immutable";
                else if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    headers.CacheControl = "no-cache";
            },
        };
    }

    /// <summary>Returns the path of the route's prerendered <c>index.html</c>, or null when the bundle has none.</summary>
    /// <param name="webRoot">The web root the bundle is served from.</param>
    /// <param name="path">The request path.</param>
    private static string? RouteDocumentPath(IFileProvider webRoot, PathString path)
    {
        var route = path.Value?.Trim('/');
        if (string.IsNullOrEmpty(route))
            return null;

        // The provider refuses paths that climb out of the web root, so a crafted path finds nothing.
        var document = $"/{route}/index.html";
        return webRoot.GetFileInfo(document) is { Exists: true, IsDirectory: false } ? document : null;
    }

    /// <summary>
    /// Builds an <see cref="SpaHostingOptions"/> instance, applying the optional caller configuration and normalizing
    /// <see cref="SpaHostingOptions.PathPrefix"/>.
    /// </summary>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The configured options.</returns>
    /// <exception cref="ArgumentException"><see cref="SpaHostingOptions.PathPrefix"/> holds <c>..</c>, a query or a fragment.</exception>
    private static SpaHostingOptions BuildOptions(Action<SpaHostingOptions>? configure)
    {
        var options = new SpaHostingOptions();
        configure?.Invoke(options);
        options.PathPrefix = NormalizePathPrefix(options.PathPrefix, nameof(configure));
        return options;
    }

    /// <summary>Normalizes a path prefix to a leading slash and no trailing slash; empty or <c>/</c> is the site root.</summary>
    /// <param name="value">The configured prefix.</param>
    /// <param name="paramName">The parameter that carried the value, for the exception.</param>
    /// <returns>The normalized prefix, or an empty string for the site root.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> holds <c>..</c>, a query or a fragment.</exception>
    private static string NormalizePathPrefix(string? value, string paramName)
    {
        var trimmed = value?.Trim().Trim('/') ?? string.Empty;
        if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.IndexOfAny(['?', '#']) >= 0)
            throw new ArgumentException($"SPA path prefix '{value}' must be a plain path without '..', a query or a fragment.", paramName);

        return trimmed.Length == 0 ? string.Empty : $"/{trimmed}";
    }
}
