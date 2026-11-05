using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Provides the singleton access-token cache: one token-endpoint call per cache key at a time, holding only tokens still live.</summary>
internal sealed class OAuth2TokenRepository : IDisposable
{
    private const int GateCount = 64;
    private const double DefaultExpiresInSeconds = 3600;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);

    /// <summary>Holds the fixed gate stripes; keys scale with sessions, so a gate per key would grow without bound.</summary>
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public OAuth2TokenRepository(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<Result<string>> GetAccessTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken)
    {
        if (ReadLiveToken(request.CacheKey) is { } cached)
        {
            return Result<string>.Ok(cached);
        }

        var gate = _gates[(uint)StringComparer.Ordinal.GetHashCode(request.CacheKey) % (uint)_gates.Length];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReadLiveToken(request.CacheKey) is { } fetchedMeanwhile)
            {
                return Result<string>.Ok(fetchedMeanwhile);
            }

            SweepExpiredTokens();

            var fetched = await FetchTokenAsync(request, cancellationToken).ConfigureAwait(false);
            if (fetched.IsFailure(out var error, out var fresh))
            {
                return Result<string>.Fail(error);
            }

            _tokens[request.CacheKey] = fresh;
            return Result<string>.Ok(fresh.AccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets the token a resource rejected; a token that already replaced it under the same key stays.</summary>
    /// <param name="cacheKey">The key the rejected token was read under.</param>
    /// <param name="accessToken">The rejected token.</param>
    public void Evict(string cacheKey, string accessToken)
    {
        if (_tokens.TryGetValue(cacheKey, out var cached) && string.Equals(cached.AccessToken, accessToken, StringComparison.Ordinal))
        {
            _tokens.TryRemove(new KeyValuePair<string, CachedToken>(cacheKey, cached));
        }
    }

    public void Dispose()
    {
        foreach (var gate in _gates)
        {
            gate.Dispose();
        }
    }

    private string? ReadLiveToken(string cacheKey) =>
        _tokens.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > _timeProvider.GetUtcNow() ? cached.AccessToken : null;

    private void SweepExpiredTokens()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in _tokens)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                // Removes this exact entry only, so a token another gate stored since the read stays.
                _tokens.TryRemove(entry);
            }
        }
    }

    private async Task<Result<CachedToken>> FetchTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(OAuth2HttpClientNames.TokenEndpoint);
        using var content = new FormUrlEncodedContent(BuildForm(request));
        using var response = await client.PostAsync(request.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var (tokenError, errorDescription) = ReadTokenError(payload);
            throw new OAuth2TokenRequestException(request.ClientName, response.StatusCode, tokenError, errorDescription);
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            return Result<CachedToken>.Fail(AppErrorFactory.ExternalUnavailable(
                $"{request.FlowName} for HTTP client '{request.ClientName}': token endpoint response has no 'access_token'."));
        }

        var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
            ? ReadExpiresIn(expiresElement)
            : DefaultExpiresInSeconds;

        var lifetime = TimeSpan.FromSeconds(expiresIn);
        var skew = request.Options.RefreshSkew < lifetime / 2 ? request.Options.RefreshSkew : lifetime / 2;
        return Result<CachedToken>.Ok(new CachedToken(accessToken, _timeProvider.GetUtcNow() + lifetime - skew));
    }

    private static List<KeyValuePair<string, string>> BuildForm(OAuth2TokenRequest request)
    {
        var options = request.Options;
        var form = new List<KeyValuePair<string, string>> { new("grant_type", request.GrantType) };
        form.AddRange(request.GrantFields);
        WriteClientAuthentication(form, options);

        if (!string.IsNullOrWhiteSpace(options.Resource))
        {
            form.Add(new("resource", options.Resource));
        }

        if (!string.IsNullOrWhiteSpace(options.Scope))
        {
            form.Add(new("scope", options.Scope));
        }

        foreach (var parameter in options.AdditionalParameters)
        {
            form.Add(new(parameter.Key, parameter.Value));
        }

        return form;
    }

    // The one place that knows how a client authenticates: private_key_jwt changes only this method and the options.
    private static void WriteClientAuthentication(List<KeyValuePair<string, string>> form, OAuth2TokenClientOptions options)
    {
        form.Add(new("client_id", options.ClientId));
        form.Add(new("client_secret", options.ClientSecret));
    }

    private static (string? Error, string? ErrorDescription) ReadTokenError(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.ValueKind == JsonValueKind.Object
                ? (ReadString(json.RootElement, "error"), ReadString(json.RootElement, "error_description"))
                : default;
        }
        catch (JsonException)
        {
            // A proxy's HTML error page is still a failed token request, only without the OAuth2 fields.
            return default;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static double ReadExpiresIn(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => DefaultExpiresInSeconds,
    };

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
