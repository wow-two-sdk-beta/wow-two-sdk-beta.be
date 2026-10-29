using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

/// <summary>
/// Maps the keys of a page's last row to an opaque page token and back: base64url of their JSON array. Keyset paging
/// over EF queries and in-memory lists writes and reads it; a validator checks a token with <see cref="TryDecode"/>.
/// </summary>
public static class PageTokenMapper
{
    /// <summary>The longest token read; a longer one is refused before it is decoded.</summary>
    public const int MaxTokenLength = 8192;

    /// <summary>The token carrying <paramref name="keys"/>.</summary>
    /// <param name="keys">The last row's keys, in the order pages follow them.</param>
    /// <returns>The opaque token.</returns>
    public static string Encode(IReadOnlyList<object?> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(keys));
    }

    /// <summary>The keys a token carries, typed as <paramref name="types"/>; a malformed token answers 400.</summary>
    /// <param name="token">The token a previous page answered with.</param>
    /// <param name="types">The key types, in order.</param>
    /// <returns>The keys.</returns>
    /// <exception cref="AppException">The token is malformed, too long or carries other keys (<c>messageKey</c> <c>PageTokenInvalid</c>).</exception>
    public static object?[] Decode(string token, IReadOnlyList<Type> types) =>
        TryDecode(token, types, out var keys)
            ? keys
            : throw AppError.Of(
                AppErrorType.Validation,
                "The page token is not valid; start again from the first page.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["messageKey"] = "PageTokenInvalid" }).ToException();

    /// <summary>Reads the keys a token carries, typed as <paramref name="types"/>, without throwing.</summary>
    /// <param name="token">The token a previous page answered with.</param>
    /// <param name="types">The key types, in order.</param>
    /// <param name="keys">The keys, when the token is well formed.</param>
    /// <returns><see langword="true"/> when the token carries one key of each type.</returns>
    public static bool TryDecode(string? token, IReadOnlyList<Type> types, [NotNullWhen(true)] out object?[]? keys)
    {
        ArgumentNullException.ThrowIfNull(types);
        keys = null;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return false;

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(token));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;
            var elements = document.RootElement.EnumerateArray().ToArray();
            if (elements.Length != types.Count)
                return false;

            keys = [.. elements.Select((element, index) => element.Deserialize(types[index]))];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
