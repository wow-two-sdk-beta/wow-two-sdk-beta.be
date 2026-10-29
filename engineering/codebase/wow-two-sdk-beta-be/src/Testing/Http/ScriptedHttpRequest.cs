namespace WoW.Two.Sdk.Beta.Be.Testing.Http;

/// <summary>One request a <see cref="ScriptedHttpMessageHandler"/> received.</summary>
public sealed record ScriptedHttpRequest
{
    /// <summary>Gets the HTTP method.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>Gets the host the request was sent to, such as <c>api.github.com</c>.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the escaped path and query without the leading slash, such as <c>repos/o/r/contents/a%20b?ref=main</c>.</summary>
    public required string PathAndQuery { get; init; }

    /// <summary>Gets the method and the path without its query, the key of <see cref="ScriptedHttpMessageHandler.Answers"/>: <c>GET repos/o/r</c>.</summary>
    public required string Route { get; init; }

    /// <summary>Gets the <c>Authorization</c> header, or <see langword="null"/> when the request had none.</summary>
    public string? Authorization { get; init; }

    /// <summary>Gets every request and content header, values comma-joined, keyed case-insensitively.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>Gets the request body as text, or <see langword="null"/> when it had none.</summary>
    public string? Body { get; init; }
}
