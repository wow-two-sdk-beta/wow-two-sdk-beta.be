namespace WoW.Two.Sdk.Beta.Be.Testing.Http;

/// <summary>Hands every client name a new <see cref="HttpClient"/> over one shared <see cref="HttpMessageHandler"/>, for code under test that takes an <see cref="IHttpClientFactory"/>.</summary>
/// <remarks>The handler is never disposed with a client, so one scripted handler serves every call of a test.</remarks>
/// <param name="handler">The handler every client sends through, such as a <see cref="ScriptedHttpMessageHandler"/>.</param>
/// <param name="baseAddress">The base address every client gets; <see langword="null"/> leaves it unset.</param>
public sealed class SingleClientHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>Gets the client names asked for, in order.</summary>
    public List<string> Names { get; } = [];

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        Names.Add(name);
        return new HttpClient(_handler, disposeHandler: false) { BaseAddress = baseAddress };
    }
}
