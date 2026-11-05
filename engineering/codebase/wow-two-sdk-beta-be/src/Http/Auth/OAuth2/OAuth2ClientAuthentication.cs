namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>
/// Provides how an OAuth2 client authenticates to its authorization server, the one place that knows it: the token request
/// uses it, and so does any other call that takes the same credentials, such as introspection. Switching a client changes
/// its options, never a caller.
/// </summary>
public static class OAuth2ClientAuthentication
{
    /// <summary>
    /// Adds the client's credentials to a request form: <c>client_id</c> with <c>client_secret</c>, or with a fresh
    /// <c>private_key_jwt</c> assertion when <see cref="OAuth2TokenClientOptions.ClientPrivateKey"/> is set.
    /// </summary>
    /// <param name="form">The form fields to add to.</param>
    /// <param name="options">The client, its credentials and the assertion's audience.</param>
    /// <param name="tokenEndpoint">The token endpoint, whose origin is the assertion's default audience.</param>
    /// <param name="now">The signing time.</param>
    public static void AddTo(ICollection<KeyValuePair<string, string>> form, OAuth2TokenClientOptions options, Uri tokenEndpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenEndpoint);

        form.Add(new("client_id", options.ClientId));
        if (string.IsNullOrWhiteSpace(options.ClientPrivateKey))
        {
            form.Add(new("client_secret", options.ClientSecret));
            return;
        }

        form.Add(new("client_assertion_type", OAuth2ClientAssertion.AssertionType));
        form.Add(new("client_assertion", OAuth2ClientAssertion.Create(options, tokenEndpoint, now)));
    }
}
