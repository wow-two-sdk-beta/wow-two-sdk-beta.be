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
    public async Task Publish_ShouldReachEverySubscriberAtOnce_SoADeadOneDelaysNoOther()
    {
        var healthy = new Uri("https://healthy.example.test/signals");
        var dead = new Uri("https://dead.example.test/signals");
        var healthyReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri == healthy)
                healthyReached.TrySetResult();
            else
                await healthyReached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); // only a concurrent fan-out gets here in time

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var signer = new RecordingSigner();
        var services = new ServiceCollection();
        services.AddWebhooks(o =>
        {
            o.Subscriptions.Add(new WebhookSubscription { Url = dead, SignatureScheme = WebhookSchemeNameConstants.KeySigned });
            o.Subscriptions.Add(new WebhookSubscription { Url = healthy, SignatureScheme = WebhookSchemeNameConstants.KeySigned });
        });
        services.AddSingleton<IWebhookSignatureIssuer>(signer);
        services.AddHttpClient(WebhookDefaultConstants.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IWebhookPublisher>().PublishAsync("session.ended", "{}"u8.ToArray()).AsTask().WaitAsync(TimeSpan.FromSeconds(8));

        healthyReached.Task.IsCompletedSuccessfully.Should().BeTrue();
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

    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request, cancellationToken);
    }
}
