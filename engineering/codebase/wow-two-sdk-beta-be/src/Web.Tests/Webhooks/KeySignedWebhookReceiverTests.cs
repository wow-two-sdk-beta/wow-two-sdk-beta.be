using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound.Validators;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Webhooks;

/// <summary>The reserved key-signed scheme inbound: no secret needed, async validation, and sync-only validators untouched.</summary>
public sealed class KeySignedWebhookReceiverTests
{
    private const string Payload = "{\"event\":\"session.ended\"}";

    [Theory]
    [InlineData("identity", WebhookSchemeNameConstants.KeySigned)]
    [InlineData("identity", "KEY-SIGNED")]
    [InlineData(WebhookSchemeNameConstants.KeySigned, null)]
    public async Task KeySignedReceiver_WithNoSecret_ShouldValidateThroughValidateAsync(string receiver, string? scheme)
    {
        var validator = new AsyncOnlyValidator();
        await using var provider = Build(receiver, new WebhookReceiverOptions { Scheme = scheme }, WebhookSchemeNameConstants.KeySigned, validator);
        using var aborted = new CancellationTokenSource();

        var receipt = await provider.GetRequiredService<WebhookReceiverService>().ReceiveAsync(Request(), receiver, aborted.Token);

        receipt.Verified.Should().BeTrue();
        receipt.DeliveryId.Should().Be("delivery-1");
        receipt.Body.ToArray().Should().Equal(Encoding.UTF8.GetBytes(Payload));
        validator.Body.Should().Be(Payload);
        validator.CancellationToken.Should().Be(aborted.Token);
    }

    [Fact]
    public async Task CustomValidator_ImplementingOnlyValidate_ShouldStillWork()
    {
        var validator = new SyncOnlyValidator();
        var settings = new WebhookReceiverOptions { Scheme = "acme" };
        settings.Secrets.Add("acme-secret");
        await using var provider = Build("partner", settings, "acme", validator);
        var receivers = provider.GetRequiredService<WebhookReceiverService>();

        (await receivers.ReceiveAsync(Request(), "partner")).Verified.Should().BeTrue();
        validator.Body.Should().Be(Payload);

        validator.Failure = WebhookSignatureFailure.Mismatch;
        (await receivers.ReceiveAsync(Request(), "partner")).Failure.Should().Be(WebhookSignatureFailure.Mismatch);
    }

    [Theory]
    [InlineData("stripe", null)]
    [InlineData("identity", WebhookSchemeNameConstants.Wow2)]
    [InlineData(WebhookSchemeNameConstants.KeySigned, WebhookSchemeNameConstants.Standard)]
    public async Task AnyOtherReceiver_WithNoSecret_ShouldFailValidation(string receiver, string? scheme)
    {
        await using var provider = Build(receiver, new WebhookReceiverOptions { Scheme = scheme }, "unused", new SyncOnlyValidator());

        var resolve = () => provider.GetRequiredService<WebhookReceiverService>().Settings(receiver);

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*needs at least one secret*");
    }

    private static ServiceProvider Build(string receiver, WebhookReceiverOptions settings, string validatorScheme, IWebhookSignatureValidator validator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInboundWebhooks(o => o.Receivers[receiver] = settings);
        services.AddKeyedSingleton(validatorScheme, validator);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Payload));
        return context;
    }

    /// <summary>Stands in for Identity's JWKS verifier: the check waits on I/O, and the sync path must not be taken.</summary>
    private sealed class AsyncOnlyValidator : IWebhookSignatureValidator
    {
        public string? Body { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public WebhookSignatureResult Validate(IHeaderDictionary headers, ReadOnlySpan<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now)
            => throw new InvalidOperationException("The receiver must call ValidateAsync.");

        public async ValueTask<WebhookSignatureResult> ValidateAsync(IHeaderDictionary headers, ReadOnlyMemory<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Body = Encoding.UTF8.GetString(body.Span);
            CancellationToken = cancellationToken;
            return new WebhookSignatureResult { DeliveryId = "delivery-1" };
        }
    }

    /// <summary>An existing validator written before <c>ValidateAsync</c>: it implements only the sync method.</summary>
    private sealed class SyncOnlyValidator : IWebhookSignatureValidator
    {
        public string? Body { get; private set; }

        public WebhookSignatureFailure Failure { get; set; }

        public WebhookSignatureResult Validate(IHeaderDictionary headers, ReadOnlySpan<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now)
        {
            Body = Encoding.UTF8.GetString(body);
            return WebhookSignatureResult.Failed(Failure);
        }
    }
}
