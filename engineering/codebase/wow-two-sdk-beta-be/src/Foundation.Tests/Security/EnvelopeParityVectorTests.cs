using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Security;

/// <summary>
/// Byte parity with secrets already in production. On 2026-10-30 the vault code these blocks were extracted from
/// (Wheelhouse's <c>SecretsVaultCrypto</c> and <c>RootKeyFingerprint</c>, compiled unchanged against
/// <c>10.0.94-beta</c>) wrapped a data key, sealed a value and made a canary under a known test root key, and printed its
/// fingerprints; these vectors are that output. The SDK must open every payload and reproduce every fingerprint exactly,
/// or a moved product could no longer read what it wrote.
/// </summary>
public sealed class EnvelopeParityVectorTests : IDisposable
{
    /// <summary>The test root key: the bytes 0 to 31.</summary>
    private static readonly byte[] Root = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];

    /// <summary>The test data key: the bytes 100 to 131.</summary>
    private static readonly byte[] DataKey = [.. Enumerable.Range(100, 32).Select(value => (byte)value)];

    private const string Secret = "s3cr3t-välue/Ω";
    private const string ProductKeyContext = "wheelhouse-vault/product-key/6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string ValueContext = "wheelhouse-vault/6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b/prod/Billing:SecretKey/v3";
    private const string CanaryKeyContext = "wheelhouse-vault/canary-key";
    private const string CanaryContext = "wheelhouse-vault/canary";
    private const string RootLabel = "wheelhouse.vault.root-key.v1";
    private const string FingerprintPurpose = "wheelhouse-vault/fingerprint";

    private static readonly EncryptedPayload WrappedDataKey =
        Payload("NwgxR9kbm8YrmPx8", "i5/uUxBp+MOwubIPPC7x2g==", "IRZvnXCXsS6ltGoCrnBvQAHErBWBOBlvgopvnBOcwrQ=");

    private static readonly EncryptedPayload SealedValue =
        Payload("EohDU+ESU05XS219", "zDb6NB5L92eNtI2mUO5lRw==", "Wg499KwVqcdHZkU30aOyTA==");

    private static readonly KeyCanary Canary = new()
    {
        WrappedKey = Payload("P7eJfq2Veb/tV6lZ", "WbPkPb3Mh6AUrfus9/sA5w==", "xUo2UHsrolMS1A3XgIUYRjiXtuilBsgoK3PZ08I3J+4="),
        Value = Payload("4PBVHRwWWzXNm1iD", "RwIl4+/6tIlVMMbyjDXNxQ==", "KEmSVmA1HzMUoX4e9j8rUOLAMUckXbE+FcsvL98Gzms="),
        Digest = Convert.FromBase64String("6HX1ME6/v6Ml7jaRH0tat9hj3ksgNOxn7h5FAapL1xU="),
    };

    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().AddEnvelopeCryptography().BuildServiceProvider();

    private IValueCipher Cipher => _services.GetRequiredService<IValueCipher>();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void AWrappedDataKeyAndAValue_ShouldOpenToWhatWasSealed()
    {
        var dataKey = Cipher.Decrypt(WrappedDataKey, Root, ProductKeyContext);

        dataKey.Should().Equal(DataKey);
        Cipher.DecryptText(SealedValue, dataKey, ValueContext).Should().Be(Secret);
    }

    [Fact]
    public void AWrappedDataKey_ShouldOpenThroughTheSealService_WhenItHoldsTheSameRoot()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IMasterKeyBroker>(new FixedBroker(Root))
            .AddEnvelopeCryptography()
            .BuildServiceProvider();
        var seal = provider.GetRequiredService<ISealService>();
        seal.Unseal();

        seal.UnwrapDataKey(WrappedDataKey, ProductKeyContext).Should().Equal(DataKey);
    }

    [Fact]
    public void ThePayloads_ShouldRefuseAnotherKeyOrContext()
    {
        var other = (byte[])Root.Clone();
        other[0] ^= 1;

        var wrongKey = () => Cipher.Decrypt(WrappedDataKey, other, ProductKeyContext);
        var wrongContext = () => Cipher.DecryptText(SealedValue, DataKey, ValueContext.Replace("/v3", "/v4", StringComparison.Ordinal));

        wrongKey.Should().Throw<CryptographicException>();
        wrongContext.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void TheCanary_ShouldVerifyUnderTheRoot_AndUnderNoOtherKeyOrContext()
    {
        var other = (byte[])Root.Clone();
        other[31] ^= 1;

        Cipher.VerifyCanary(Canary, Root, CanaryKeyContext, CanaryContext).Should().BeTrue();
        Cipher.VerifyCanary(Canary, other, CanaryKeyContext, CanaryContext).Should().BeFalse();
        Cipher.VerifyCanary(Canary, Root, CanaryContext, CanaryKeyContext).Should().BeFalse();
        Cipher.VerifyCanary(Canary with { Digest = new byte[32] }, Root, CanaryKeyContext, CanaryContext).Should().BeFalse();
    }

    [Fact]
    public void TheFingerprints_ShouldBeTheOnesTheSourcePrinted()
    {
        var values = new ValueFingerprintHasher(FingerprintPurpose);

        new KeyFingerprintHasher(RootLabel).Hash(Root).Should().Be("aeb1 80f7 9014 1a39 9680");
        values.Hash(Secret, DataKey).Should().Be("9d24cc8cfde8328a");
        values.Hash("", DataKey).Should().Be("91870820982a2299");
    }

    private static EncryptedPayload Payload(string nonce, string tag, string cipherText) => new()
    {
        Nonce = Convert.FromBase64String(nonce),
        Tag = Convert.FromBase64String(tag),
        CipherText = Convert.FromBase64String(cipherText),
    };

    /// <summary>Hands the seal service a copy of one key.</summary>
    private sealed class FixedBroker(byte[] key) : IMasterKeyBroker
    {
        public string Location => "fixed";

        public MasterKeyRead Read() => new() { Key = (byte[])key.Clone(), Stamp = "fixed" };

        public string? CheckAccess() => null;
    }
}
