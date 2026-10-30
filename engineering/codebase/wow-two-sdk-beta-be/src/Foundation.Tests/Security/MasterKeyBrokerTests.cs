using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Security;

/// <summary>
/// Verifies the re-read master key: the file reads as its key, a fresh copy each time, or as why there is none; a
/// changed file changes the stamp; the access check passes an owner-only file; the unsealer proves each new content once;
/// a canary made by the SDK verifies; and the fingerprints separate keys and labels.
/// </summary>
public sealed class MasterKeyBrokerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("sdk-master-key-").FullName;

    private string KeyFile => Path.Combine(_folder, "root.key");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AKeyFile_ShouldReadAsItsKey_AFreshCopyEachTime()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(KeyFile, Convert.ToBase64String(key) + "\n");
        var source = new FileMasterKeyBroker(KeyFile);

        var first = source.Read();

        first.Key.Should().Equal(key);
        first.Problem.Should().BeNull();
        first.ToString().Should().Be("MasterKeyRead { present }").And.NotContain(Convert.ToBase64String(key));
        first.Key![0] ^= 0xFF;
        source.Read().Key.Should().Equal(key, "a caller clearing its copy leaves the next read whole");
        source.TryLoadKey().Should().Equal(key);
        source.Location.Should().Be(KeyFile);
    }

    [Fact]
    public void AMissingOrMalformedFile_ShouldSayWhy()
    {
        var source = new FileMasterKeyBroker(KeyFile);

        source.Read().Should().Match<MasterKeyRead>(read => read.Key == null && read.Problem == MasterKeyRead.Missing);
        source.TryLoadKey().Should().BeNull();
        foreach (var content in new[] { "", "not base64!", Convert.ToBase64String(new byte[16]), Convert.ToBase64String(new byte[33]), new string('A', 1100) })
        {
            File.WriteAllText(KeyFile, content);
            source.Read().Problem.Should().Be(MasterKeyRead.Invalid, content);
        }

        var load = () => source.TryLoadKey();
        load.Should().Throw<MasterKeyFormatException>().Which.Message.Should().NotContain("AAAA");
    }

    [Fact]
    public void TheAccessCheck_ShouldPassAnOwnerOnlyFile_AndNameAMissingUnreadableOrExposedOne()
    {
        var source = new FileMasterKeyBroker(KeyFile);
        source.CheckAccess().Should().Be(MasterKeyRead.Missing);
        File.WriteAllText(KeyFile, Convert.ToBase64String(new byte[32]));
        if (OperatingSystem.IsWindows())
            return;

        foreach (var mode in new[] { UnixFileMode.UserRead, UnixFileMode.UserRead | UnixFileMode.UserWrite })
        {
            File.SetUnixFileMode(KeyFile, mode);
            source.CheckAccess().Should().BeNull(mode.ToString());
        }

        foreach (var mode in new[] { UnixFileMode.UserRead | UnixFileMode.GroupRead, UnixFileMode.UserRead | UnixFileMode.OtherRead,
                     UnixFileMode.UserRead | UnixFileMode.OtherWrite })
        {
            File.SetUnixFileMode(KeyFile, mode);
            source.CheckAccess().Should().Be(MasterKeyRead.Exposed, mode.ToString());
        }

        File.SetUnixFileMode(KeyFile, UnixFileMode.None);
        if (Environment.UserName != "root")
        {
            source.CheckAccess().Should().Be(MasterKeyRead.Unreadable, "the process's user may not open it");
            source.Read().Problem.Should().Be(MasterKeyRead.Missing);
        }

        File.SetUnixFileMode(KeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void AChangedFile_ShouldChangeTheStamp()
    {
        File.WriteAllText(KeyFile, Convert.ToBase64String(new byte[32]));
        var source = new FileMasterKeyBroker(KeyFile);
        var before = source.Read().Stamp;

        File.WriteAllText(KeyFile, Convert.ToBase64String(new byte[32]) + "\n");
        File.SetLastWriteTimeUtc(KeyFile, DateTime.UtcNow.AddMinutes(1));

        source.Read().Stamp.Should().NotBe(before).And.MatchRegex("^[0-9]+:[0-9]+$");
    }

    [Fact]
    public async Task TheUnsealer_ShouldProveEachNewContentOnce_AndClearAKeyThatFailsItsProof()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(KeyFile, Convert.ToBase64String(key));
        var proofs = new MasterKeyProofTracker();
        var unsealer = new MasterKeyUnsealService(new FileMasterKeyBroker(KeyFile), proofs);
        var proved = 0;
        byte[]? seen = null;

        Task<bool> Prove(byte[] candidate, CancellationToken ct)
        {
            proved++;
            seen = candidate;
            return Task.FromResult(candidate.SequenceEqual(key));
        }

        (await unsealer.UnsealAsync(Prove, CancellationToken.None)).Key.Should().Equal(key);
        (await unsealer.UnsealAsync(Prove, CancellationToken.None)).Key.Should().Equal(key);
        proved.Should().Be(1);

        File.WriteAllText(KeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        File.SetLastWriteTimeUtc(KeyFile, DateTime.UtcNow.AddMinutes(1));
        var mismatch = await unsealer.UnsealAsync(Prove, CancellationToken.None);

        mismatch.Should().Match<MasterKeyRead>(read => read.Key == null && read.Problem == MasterKeyRead.Mismatch);
        seen.Should().OnlyContain(b => b == 0, "a refused key is cleared");
        proofs.IsProven(mismatch.Stamp).Should().BeFalse();

        File.Delete(KeyFile);
        (await unsealer.UnsealAsync(Prove, CancellationToken.None)).Problem.Should().Be(MasterKeyRead.Missing);
        proved.Should().Be(2);
    }

    [Fact]
    public async Task TheUnsealer_ShouldFailAProofThatCannotDecrypt_AndPropagateAnyOtherFailure()
    {
        File.WriteAllText(KeyFile, Convert.ToBase64String(new byte[32]));
        var unsealer = new MasterKeyUnsealService(new FileMasterKeyBroker(KeyFile), new MasterKeyProofTracker());
        byte[]? seen = null;

        var refused = await unsealer.UnsealAsync((_, _) => throw new AuthenticationTagMismatchException(), CancellationToken.None);
        var failing = () => unsealer.UnsealAsync(
            (candidate, _) =>
            {
                seen = candidate;
                return Task.FromException<bool>(new InvalidOperationException("store down"));
            },
            CancellationToken.None);

        refused.Problem.Should().Be(MasterKeyRead.Mismatch);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        seen.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void ACanaryTheSdkMakes_ShouldVerifyUnderItsKeyOnly()
    {
        using var provider = new ServiceCollection().AddLogging().AddEnvelopeCryptography().BuildServiceProvider();
        var cipher = provider.GetRequiredService<IValueCipher>();
        var root = RandomNumberGenerator.GetBytes(32);

        var canary = cipher.CreateCanary(root, "app/canary-key", "app/canary");

        cipher.VerifyCanary(canary, root, "app/canary-key", "app/canary").Should().BeTrue();
        cipher.VerifyCanary(canary, RandomNumberGenerator.GetBytes(32), "app/canary-key", "app/canary").Should().BeFalse();
        canary.Digest.Should().HaveCount(32);
        canary.WrappedKey.CipherText.Should().HaveCount(32);
    }

    [Fact]
    public void TheFingerprints_ShouldSeparateKeysAndLabels()
    {
        var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var other = (byte[])key.Clone();
        other[31] ^= 1;
        var keys = new KeyFingerprintHasher("app.root.v1");
        var values = new ValueFingerprintHasher("app/fingerprint");

        var labelled = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.ASCII.GetBytes("app.root.v1"), 0, .. key]))[..20];

        keys.Hash(key).Replace(" ", "", StringComparison.Ordinal).Should().Be(labelled);
        keys.Hash(key).Should().MatchRegex(KeyFingerprintHasher.Pattern);
        keys.Hash(other).Should().NotBe(keys.Hash(key));
        new KeyFingerprintHasher("app.root.v2").Hash(key).Should().NotBe(keys.Hash(key));
        values.Hash("x", key).Should().NotBe(values.Hash("x", other));
        values.Hash("x", key).Should().NotBe(new ValueFingerprintHasher("app/other").Hash("x", key));
        var unlabelled = () => new KeyFingerprintHasher("");
        unlabelled.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheEnvironmentBroker_ShouldReadTheVariableAnew_WithAStampThatFollowsItsValue()
    {
        var name = "SDK_TEST_MASTER_KEY_" + Guid.NewGuid().ToString("N");
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddEnvelopeCryptography(o => o.MasterKeyEnvironmentVariable = name)
            .BuildServiceProvider();
        var broker = provider.GetRequiredService<IMasterKeyBroker>();
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            broker.Read().Problem.Should().Be(MasterKeyRead.Missing);
            broker.CheckAccess().Should().Be(MasterKeyRead.Missing);

            Environment.SetEnvironmentVariable(name, Convert.ToBase64String(key));
            var first = broker.Read();
            Environment.SetEnvironmentVariable(name, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var second = broker.Read();
            Environment.SetEnvironmentVariable(name, "not base64!");

            first.Key.Should().Equal(key);
            first.Stamp.Should().NotBe(second.Stamp).And.NotContain(Convert.ToBase64String(key));
            broker.CheckAccess().Should().BeNull();
            broker.Read().Problem.Should().Be(MasterKeyRead.Invalid);
            broker.Location.Should().Be("env:" + name);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void AddFileMasterKeyBroker_ShouldReplaceTheEnvironmentBroker_InEitherOrder()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(KeyFile, Convert.ToBase64String(key));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EnvelopeCryptography:KeyFile:Path"] = KeyFile })
            .Build();

        using var after = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddEnvelopeCryptography()
            .AddFileMasterKeyBroker()
            .BuildServiceProvider();
        using var before = new ServiceCollection()
            .AddLogging()
            .AddFileMasterKeyBroker(o => o.Path = KeyFile)
            .AddEnvelopeCryptography()
            .BuildServiceProvider();

        foreach (var provider in new[] { after, before })
        {
            provider.GetRequiredService<IMasterKeyBroker>().Should().BeOfType<FileMasterKeyBroker>().Which.Location.Should().Be(KeyFile);
            provider.GetRequiredService<MasterKeyUnsealService>().Should().NotBeNull();
            var seal = provider.GetRequiredService<ISealService>();
            seal.Unseal();
            seal.IsSealed.Should().BeFalse();
        }
    }

    [Fact]
    public void AddFileMasterKeyBroker_ShouldRefuseARelativePath()
    {
        using var provider = new ServiceCollection().AddFileMasterKeyBroker(o => o.Path = "root.key").BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IMasterKeyBroker>();

        resolve.Should().Throw<OptionsValidationException>();
    }
}
