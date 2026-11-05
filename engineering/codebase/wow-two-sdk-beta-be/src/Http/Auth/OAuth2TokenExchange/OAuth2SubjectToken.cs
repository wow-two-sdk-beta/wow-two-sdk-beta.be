namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;

/// <summary>Represents the person's token a call exchanges, and the session its exchanged token is cached under.</summary>
public sealed record OAuth2SubjectToken
{
    /// <summary>Gets the person's access token, sent as <c>subject_token</c>; never cached or logged.</summary>
    public required string Token { get; init; }

    /// <summary>Gets what separates this person's session in the cache, such as the <c>sid</c> claim or a hash; never the token itself.</summary>
    public required string SessionKey { get; init; }

    /// <summary>Returns the record's text without <see cref="Token"/>, so logging it cannot leak the token.</summary>
    /// <returns>The session key in the record's usual text form.</returns>
    public override string ToString() => $"{nameof(OAuth2SubjectToken)} {{ {nameof(SessionKey)} = {SessionKey} }}";
}
