using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Connects to Meta's Instagram Business Discovery API for bounded professional-account media pages.</summary>
public sealed partial class InstagramClient : IInstagramClient
{
    private static readonly ActivitySource Activities = new("WoW.Two.Sdk.Beta.Be.Integrations.Instagram");
    private static readonly string[] MediaFields =
        ["id", "caption", "timestamp", "permalink", "media_type", "media_url", "thumbnail_url"];
    private static readonly string[] ChildFields = ["id", "media_type", "media_url", "thumbnail_url"];
    private readonly HttpClient _http;
    private readonly IAccessTokenService _tokens;
    private readonly InstagramIntegrationOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates a client over an explicitly selected transport and token source.</summary>
    /// <param name="http">The factory-managed transport; use the supplied registration for safety and single-attempt bounds.</param>
    /// <param name="tokenProvider">The Instagram-specific token source, read once per call.</param>
    /// <param name="options">Validated transport bounds, copied for this client.</param>
    /// <param name="clock">The clock used to interpret provider retry dates.</param>
    public InstagramClient(HttpClient http, IAccessTokenService tokenProvider,
        InstagramIntegrationOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        if (!options.IsValid())
            throw new ArgumentException("Instagram requires the HTTPS Graph origin and valid transport bounds.", nameof(options));
        _http = http;
        _tokens = tokenProvider;
        _options = options with { };
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<InstagramMediaPage, InstagramFailure>> GetBusinessDiscoveryMediaAsync(
        InstagramMediaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using Activity? activity = Activities.StartActivity("instagram.business-discovery");
        var diagnostics = new InstagramRequestDiagnostics();

        Result<InstagramMediaPage, InstagramFailure> Failure(InstagramFailureKind kind)
        {
            if (kind == InstagramFailureKind.RateLimited && diagnostics.RetryAfter is null)
                diagnostics = diagnostics with { RetryAfter = TimeSpan.FromMinutes(15) };
            activity?.SetTag("instagram.failure", kind.ToString());
            activity?.SetStatus(ActivityStatusCode.Error);
            return Result<InstagramMediaPage, InstagramFailure>.Fail(new()
            {
                Kind = kind, Message = Message(kind), Diagnostics = diagnostics
            });
        }

        if (!NumericId().IsMatch(request.ProfessionalAccountId ?? string.Empty)
            || !Version().IsMatch(request.ApiVersion ?? string.Empty)
            || !Username().IsMatch(request.Username ?? string.Empty)
            || request.PageSize is < 1 or > 50
            || (request.After is not null && !Cursor().IsMatch(request.After)))
            return Failure(InstagramFailureKind.InvalidRequest);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        try
        {
            string? token = await _tokens.GetAccessTokenAsync(deadline.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token) || token.Length > 8192 || !token.All(value => value is > (char)32 and < (char)127))
                return Failure(InstagramFailureKind.CredentialsMissing);
            deadline.Token.ThrowIfCancellationRequested();

            var endpoint = new Uri(_options.BaseAddress!, $"{request.ApiVersion}/{request.ProfessionalAccountId}");
            diagnostics = diagnostics with { RequestUrl = endpoint.AbsoluteUri };
            string media = "media.limit(" + request.PageSize.ToString(CultureInfo.InvariantCulture) + ")"
                + (request.After is null ? string.Empty : $".after({request.After})");
            string fields = $"business_discovery.username({request.Username}){{username,{media}{{id,caption,timestamp,permalink,media_type,media_url,thumbnail_url,children{{id,media_type,media_url,thumbnail_url}}}}}}";
            var uri = new Uri(endpoint.AbsoluteUri + "?fields=" + Uri.EscapeDataString(fields));
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            diagnostics = diagnostics with { Requests = 1 };
            using HttpResponseMessage response = await _http.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            TimeSpan? retry = response.Headers.RetryAfter?.Delta;
            if (response.Headers.RetryAfter?.Date is { } date)
                retry = date - _clock.GetUtcNow();
            if (retry < TimeSpan.Zero)
                retry = TimeSpan.Zero;
            diagnostics = diagnostics with { HttpStatus = (int)response.StatusCode, RetryAfter = retry };
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            if ((int)response.StatusCode is >= 300 and < 400)
                return Failure(InstagramFailureKind.InvalidRedirect);
            if (response.Content.Headers.ContentLength > _options.MaximumResponseBytes)
                return Failure(InstagramFailureKind.ResponseTooLarge);

            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block.AsMemory(0,
                       (int)Math.Min(block.Length, _options.MaximumResponseBytes - diagnostics.Bytes + 1)),
                       deadline.Token).ConfigureAwait(false)) > 0)
            {
                diagnostics = diagnostics with { Bytes = diagnostics.Bytes + count };
                if (diagnostics.Bytes > _options.MaximumResponseBytes)
                    return Failure(InstagramFailureKind.ResponseTooLarge);
                buffer.Write(block, 0, count);
            }

            InstagramFailureKind? statusFailure = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => InstagramFailureKind.RateLimited,
                HttpStatusCode.Unauthorized => InstagramFailureKind.LoginRequired,
                HttpStatusCode.Forbidden => InstagramFailureKind.Restricted,
                _ => null
            };
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
                    new JsonDocumentOptions { MaxDepth = _options.MaximumJsonDepth });
            }
            catch (JsonException)
            {
                return Failure(statusFailure ?? (response.IsSuccessStatusCode
                    ? InstagramFailureKind.UnexpectedContent : InstagramFailureKind.HttpError));
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement error))
                {
                    int? code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out JsonElement value)
                        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed) ? parsed : null;
                    diagnostics = diagnostics with { ProviderCode = code };
                    return Failure(code switch
                    {
                        190 => InstagramFailureKind.LoginRequired,
                        4 or 17 or 32 or 613 or 80004 => InstagramFailureKind.RateLimited,
                        10 or 100 or 200 => InstagramFailureKind.Restricted,
                        _ => statusFailure ?? InstagramFailureKind.ProviderFailed
                    });
                }
                if (statusFailure is { } status)
                    return Failure(status);
                if (!response.IsSuccessStatusCode)
                    return Failure(InstagramFailureKind.HttpError);
                if (root.ValueKind != JsonValueKind.Object)
                    return Failure(InstagramFailureKind.UnexpectedContent);
                if (!root.TryGetProperty("business_discovery", out JsonElement discovery)
                    || discovery.ValueKind == JsonValueKind.Null)
                    return Failure(InstagramFailureKind.Restricted);
                if (discovery.ValueKind != JsonValueKind.Object
                    || !string.Equals(Text(discovery, "username"), request.Username, StringComparison.OrdinalIgnoreCase)
                    || !discovery.TryGetProperty("media", out JsonElement page) || page.ValueKind != JsonValueKind.Object
                    || !page.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array
                    || data.GetArrayLength() > request.PageSize)
                    return Failure(InstagramFailureKind.UnexpectedContent);

                string? after = data.GetArrayLength() == 0 ? null : ReadCursor(page, request.After);
                JsonElement[] records = [.. data.EnumerateArray().Select(ProjectMedia)];
                deadline.Token.ThrowIfCancellationRequested();
                activity?.SetTag("instagram.media.count", records.Length);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Result<InstagramMediaPage, InstagramFailure>.Ok(new()
                {
                    Media = records, After = records.Length == 0 ? null : after, Diagnostics = diagnostics
                });
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(InstagramFailureKind.Timeout);
        }
        catch (TimeoutRejectedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(InstagramFailureKind.Timeout);
        }
        catch (BrokenCircuitException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics = diagnostics with { Requests = 0 };
            return Failure(InstagramFailureKind.ProviderFailed);
        }
        catch (HttpRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(InstagramFailureKind.NetworkError);
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(InstagramFailureKind.NetworkError);
        }
        catch (JsonException)
        {
            return Failure(InstagramFailureKind.UnexpectedContent);
        }
    }

    private static string? ReadCursor(JsonElement page, string? previous)
    {
        if (!page.TryGetProperty("paging", out JsonElement paging))
            return null;
        if (paging.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        string? cursor = null;
        if (paging.TryGetProperty("cursors", out JsonElement cursors))
        {
            if (cursors.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            if (cursors.TryGetProperty("after", out JsonElement after))
            {
                if (after.ValueKind != JsonValueKind.String)
                    throw new JsonException();
                cursor = after.GetString();
                if (cursor is null || !Cursor().IsMatch(cursor) || cursor == previous)
                    throw new JsonException();
            }
        }
        if (cursor is null && paging.TryGetProperty("next", out JsonElement next) && next.ValueKind != JsonValueKind.Null)
            throw new JsonException();
        return cursor;
    }

    private static JsonElement ProjectMedia(JsonElement record)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteMedia(writer, record, includeChildren: true);
        }
        using JsonDocument document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
        return document.RootElement.Clone();
    }

    private static void WriteMedia(Utf8JsonWriter writer, JsonElement record, bool includeChildren)
    {
        if (record.ValueKind != JsonValueKind.Object || Text(record, "id") is not { } id || !NumericId().IsMatch(id))
            throw new JsonException();
        writer.WriteStartObject();
        foreach (string field in includeChildren ? MediaFields : ChildFields)
        {
            if (!record.TryGetProperty(field, out JsonElement value))
                continue;
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new JsonException();
            writer.WritePropertyName(field);
            value.WriteTo(writer);
        }
        if (includeChildren && record.TryGetProperty("children", out JsonElement children))
        {
            if (children.ValueKind != JsonValueKind.Object || !children.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 50)
                throw new JsonException();
            writer.WriteStartObject("children");
            writer.WriteStartArray("data");
            foreach (JsonElement child in data.EnumerateArray())
                WriteMedia(writer, child, includeChildren: false);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static string? Text(JsonElement node, string property) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Message(InstagramFailureKind kind) => kind switch
    {
        InstagramFailureKind.InvalidRequest => "Instagram request identity, page size or cursor is invalid.",
        InstagramFailureKind.CredentialsMissing => "Instagram credentials are unavailable or invalid.",
        InstagramFailureKind.LoginRequired => "Instagram credentials need renewal.",
        InstagramFailureKind.Restricted => "Instagram permissions or publisher eligibility do not permit this request.",
        InstagramFailureKind.RateLimited => "Instagram rate limit; wait before another request.",
        InstagramFailureKind.InvalidRedirect => "Instagram API redirects are not followed.",
        InstagramFailureKind.ResponseTooLarge => "Instagram response exceeded the configured byte limit.",
        InstagramFailureKind.UnexpectedContent => "Instagram returned invalid bounded media JSON or pagination.",
        InstagramFailureKind.HttpError => "Instagram returned a failing HTTP status.",
        InstagramFailureKind.ProviderFailed => "Instagram returned an API failure or its request circuit is open.",
        InstagramFailureKind.NetworkError => "Instagram API connection failed.",
        InstagramFailureKind.Timeout => "Instagram API request timed out.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [GeneratedRegex(@"\A[a-zA-Z0-9_.]{1,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Username();
    [GeneratedRegex(@"\A[0-9]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumericId();
    [GeneratedRegex(@"\Av[0-9]{1,3}\.0\z", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
    [GeneratedRegex(@"\A[a-zA-Z0-9_+=/\-]{1,4096}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Cursor();
}
