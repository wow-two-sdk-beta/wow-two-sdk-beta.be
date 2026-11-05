using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

/// <summary>The public client authentication a service's own call, such as introspection, shares with the token request.</summary>
public sealed class OAuth2ClientAuthenticationTests
{
    [Fact]
    public void AddTo_ShouldWriteTheSecret_OrAnAssertionWhenAKeyIsSet()
    {
        var endpoint = new Uri("https://issuer.example.test/connect/token");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var secretForm = new List<KeyValuePair<string, string>> { new("token", "opaque") };
        OAuth2ClientAuthentication.AddTo(secretForm, new OAuth2ClientCredentialsOptions { ClientId = "service", ClientSecret = "secret-value" }, endpoint, now);

        secretForm.Should().Equal(KeyValuePair.Create("token", "opaque"), KeyValuePair.Create("client_id", "service"), KeyValuePair.Create("client_secret", "secret-value"));

        var keyForm = new List<KeyValuePair<string, string>>();
        OAuth2ClientAuthentication.AddTo(keyForm, new OAuth2ClientCredentialsOptions { ClientId = "service", ClientPrivateKey = key.ExportPkcs8PrivateKeyPem() }, endpoint, now);

        var fields = keyForm.ToDictionary(field => field.Key, field => field.Value);
        fields.Should().NotContainKey("client_secret");
        fields["client_id"].Should().Be("service");
        fields["client_assertion_type"].Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(fields["client_assertion"].Split('.')[1])).RootElement;
        payload.GetProperty("sub").GetString().Should().Be("service");
        payload.GetProperty("aud").GetString().Should().Be("https://issuer.example.test/");
        payload.GetProperty("iat").GetInt64().Should().Be(now.ToUnixTimeSeconds());
    }
}
