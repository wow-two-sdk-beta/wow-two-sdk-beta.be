using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

/// <summary>Signs the short JWT a client authenticates with under <c>private_key_jwt</c> (RFC 7523).</summary>
internal static class OAuth2ClientAssertion
{
    /// <summary>The <c>client_assertion_type</c> value.</summary>
    public const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private const string TokenType = "client-authentication+jwt";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Creates a fresh assertion: <c>iss</c> and <c>sub</c> name the client, <c>aud</c> the authorization server.</summary>
    /// <param name="options">The client, its key and the assertion's audience.</param>
    /// <param name="tokenEndpoint">The token endpoint, whose origin is the default audience.</param>
    /// <param name="now">The signing time.</param>
    /// <returns>The compact JWT.</returns>
    public static string Create(OAuth2TokenClientOptions options, Uri tokenEndpoint, DateTimeOffset now)
    {
        var pem = options.ClientPrivateKey ?? throw new InvalidOperationException("No client private key is configured.");
        using var rsa = TryLoadRsa(pem);
        using var ecdsa = rsa is null ? TryLoadEcdsa(pem) : null;
        var algorithm = rsa is not null ? "RS256" : ecdsa?.KeySize switch
        {
            256 => "ES256",
            384 => "ES384",
            521 => "ES512",
            _ => throw new InvalidOperationException($"OAuth2 client '{options.ClientId}': ClientPrivateKey is not a usable RSA or P-256, P-384 or P-521 private key."),
        };

        var header = new Dictionary<string, string> { ["alg"] = algorithm, ["typ"] = TokenType };
        if (!string.IsNullOrWhiteSpace(options.ClientPrivateKeyId))
        {
            header["kid"] = options.ClientPrivateKeyId;
        }

        var payload = new Dictionary<string, object>
        {
            ["iss"] = options.ClientId,
            ["sub"] = options.ClientId,
            ["aud"] = options.ClientAssertionAudience ?? new Uri(tokenEndpoint, "/").AbsoluteUri,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + Lifetime).ToUnixTimeSeconds(),
        };

        var signedPart = $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var bytes = System.Text.Encoding.ASCII.GetBytes(signedPart);
        var signature = rsa is not null
            ? rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : ecdsa!.SignData(bytes, ecdsa.KeySize switch { 256 => HashAlgorithmName.SHA256, 384 => HashAlgorithmName.SHA384, _ => HashAlgorithmName.SHA512 });
        return $"{signedPart}.{Base64Url.EncodeToString(signature)}";
    }

    private static RSA? TryLoadRsa(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            return null;
        }
    }

    private static ECDsa? TryLoadEcdsa(string pem)
    {
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(pem);
            return ecdsa;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            ecdsa.Dispose();
            return null;
        }
    }
}
