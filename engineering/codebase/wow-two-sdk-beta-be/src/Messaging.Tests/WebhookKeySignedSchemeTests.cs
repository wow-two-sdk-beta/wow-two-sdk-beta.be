using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Issuers;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>The reserved key-signed scheme outbound: no secret needed, the signer sees the target, every other scheme still needs a secret.</summary>
public sealed class WebhookKeySignedSchemeTests
{
    private static readonly Uri Target = new("https://receiver.example.test/signals");

    [Theory]
    [InlineData(WebhookSchemeNameConstants.KeySigned)]
    [InlineData("KEY-SIGNED")]
    public async Task KeySignedSubscription_WithNoSecret_ShouldDeliver_AndItsSignerSeesTheUrl(string scheme)
    {
        var signer = new RecordingSigner();
        var handler = new CapturingHandler();
        await using var provider = Build(handler, signer, o => o.Subscriptions.Add(new WebhookSubscription { Url = Target, SignatureScheme = scheme }));

        await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("session.ended", "{}"u8.ToArray());

        var signed = signer.Signed.Should().ContainSingle().Subject;
        signed.Url.Should().Be(Target);
        signed.Secret.Should().BeEmpty();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request["Authorization"].Should().Be("Signed " + signed.DeliveryId);
        request[WebhookHeaderConstants.Event].Should().Be("session.ended");
    }

    [Fact]
    public async Task KeySignedAsTheDefaultScheme_ShouldExemptASubscriptionNamingNone()
    {
        var signer = new RecordingSigner();
        var handler = new CapturingHandler();
        await using var provider = Build(handler, signer, o =>
        {
            o.SignatureScheme = WebhookSchemeNameConstants.KeySigned;
            o.Subscriptions.Add(new WebhookSubscription { Url = Target });
        });

        await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("session.ended", "{}"u8.ToArray());

        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WebhookSchemeNameConstants.Wow2)]
    [InlineData(WebhookSchemeNameConstants.Standard)]
    [InlineData("acme")]
    public async Task AnyOtherScheme_WithNoSecret_ShouldFailValidation(string? scheme)
    {
        await using var provider = Build(new CapturingHandler(), new RecordingSigner(), o => o.Subscriptions.Add(new WebhookSubscription { Url = Target, SignatureScheme = scheme }));

        var resolve = () => provider.GetRequiredService<WebhookOptions>();

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*every subscription must have a secret*");
    }

    [Fact]
    public async Task NamedSchemeWithNoSecret_ShouldFailValidation_EvenUnderAKeySignedDefault()
    {
        await using var provider = Build(new CapturingHandler(), new RecordingSigner(), o =>
        {
            o.SignatureScheme = WebhookSchemeNameConstants.KeySigned;
            o.Subscriptions.Add(new WebhookSubscription { Url = Target, SignatureScheme = WebhookSchemeNameConstants.Wow2 });
        });

        var resolve = () => provider.GetRequiredService<WebhookOptions>();

        resolve.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task SubscriptionStoredAtRuntime_WithNoSecret_ShouldDrop_NeverSignUnderAnEmptyKey()
    {
        var handler = new CapturingHandler();
        await using var provider = Build(handler, new RecordingSigner());
        await provider.GetRequiredService<IWebhookSubscriptionRepository>().AddAsync(new WebhookSubscription { Url = Target }, CancellationToken.None);

        await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("order.created", "{}"u8.ToArray());

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task KeySignedSubscription_WithoutASigner_ShouldDropWithoutSending()
    {
        var handler = new CapturingHandler();
        await using var provider = Build(handler, signer: null, o => o.Subscriptions.Add(new WebhookSubscription { Url = Target, SignatureScheme = WebhookSchemeNameConstants.KeySigned }));

        await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("session.ended", "{}"u8.ToArray());

        handler.Requests.Should().BeEmpty("core ships no key-signed signer");
    }

    private static ServiceProvider Build(CapturingHandler handler, RecordingSigner? signer, Action<WebhookOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (signer is not null)
            services.AddSingleton<IWebhookSignatureIssuer>(signer);
        services.AddWebhooks(configure);
        services.AddHttpClient(WebhookDefaultConstants.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    /// <summary>Stands in for Identity's signer: records what it signs and sends a header derived from it.</summary>
    private sealed class RecordingSigner : IWebhookSignatureIssuer
    {
        public List<WebhookSigningModel> Signed { get; } = [];

        public string Scheme => WebhookSchemeNameConstants.KeySigned;

        public IReadOnlyDictionary<string, string> Issue(WebhookSigningModel signing)
        {
            Signed.Add(signing);
            return new Dictionary<string, string> { ["Authorization"] = "Signed " + signing.DeliveryId };
        }
    }

    /// <summary>Records each request's headers and answers 200.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
