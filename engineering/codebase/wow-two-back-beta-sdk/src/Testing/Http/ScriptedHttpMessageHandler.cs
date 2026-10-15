using System.Net;
using System.Text;

namespace WoW.Two.Sdk.Backend.Beta.Testing.Http;

/// <summary>Answers outbound HTTP calls from a script and records every request, so a test can tell what was asked and how often.</summary>
/// <remarks>
///   - keys are the escaped path and query without the leading slash, such as <c>repos/o/r/commits/main</c>
///   - per request, <see cref="Failures"/> wins over <see cref="Json"/>, which wins over <see cref="Raw"/>
///   - a path nobody scripted answers <c>404 Not Found</c>
///   - hand it to a client with <c>ConfigurePrimaryHttpMessageHandler(() =&gt; handler)</c> or <see cref="SingleClientHttpClientFactory"/>
/// </remarks>
public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<ScriptedHttpRequest> _requests = [];

    /// <summary>Gets the JSON answers (<c>200</c>, <c>application/json</c>), by path and query.</summary>
    public Dictionary<string, string> Json { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the raw byte answers (<c>200</c>, no media type), by path and query.</summary>
    public Dictionary<string, byte[]> Raw { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the paths that answer a bare status instead of what was scripted.</summary>
    public Dictionary<string, HttpStatusCode> Failures { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets every request received, in the order asked.</summary>
    public IReadOnlyList<ScriptedHttpRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>Returns how many requests asked for <paramref name="pathAndQuery"/>.</summary>
    /// <param name="pathAndQuery">The path and query without the leading slash.</param>
    public int CountOf(string pathAndQuery) =>
        Requests.Count(request => string.Equals(request.PathAndQuery, pathAndQuery, StringComparison.Ordinal));

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = request.RequestUri!.PathAndQuery.TrimStart('/');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Headers)
            headers[name] = string.Join(", ", values);
        if (request.Content is not null)
        {
            foreach (var (name, values) in request.Content.Headers)
                headers[name] = string.Join(", ", values);
        }

        var recorded = new ScriptedHttpRequest
        {
            Method = request.Method,
            PathAndQuery = key,
            Authorization = request.Headers.Authorization?.ToString(),
            Headers = headers,
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
        };
        lock (_gate)
            _requests.Add(recorded);

        if (Failures.TryGetValue(key, out var failure))
            return new HttpResponseMessage(failure) { RequestMessage = request };
        if (Json.TryGetValue(key, out var json))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }

        return Raw.TryGetValue(key, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
    }
}
