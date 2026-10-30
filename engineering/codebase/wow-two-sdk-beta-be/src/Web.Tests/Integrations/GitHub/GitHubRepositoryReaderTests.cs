using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Caching.Hybrid;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Integrations.GitHub;

/// <summary>
/// Verifies the commit-keyed reader over the multi-repository fake: a commit's verdict is kept for good, no verdict is
/// read again, a branch head is kept only for <see cref="GitHubRepositoryReaderOptions.HeadFor"/>, and a fresh read
/// replaces the kept head unless it fails.
/// </summary>
public sealed class GitHubRepositoryReaderTests : IDisposable
{
    private const string Repository = "owner/pilot";
    private const string Descriptor = "deploy/deploy.yml";

    private readonly FakeGitHubClient _github = new FakeGitHubClient().WithPushedOnly();
    private readonly ServiceProvider _services;

    public GitHubRepositoryReaderTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCaching();
        _services = services.BuildServiceProvider();
        Cache = _services.GetRequiredService<ICacheRepository>();
    }

    private ICacheRepository Cache { get; }

    public void Dispose() => _services.Dispose();

    private GitHubRepositoryReader Reader(Action<GitHubRepositoryReaderOptions>? configure = null)
    {
        var options = new GitHubRepositoryReaderOptions();
        configure?.Invoke(options);
        return new GitHubRepositoryReader(_github, Cache, options);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadOnceAndKeepTheVerdict_ForFoundMissingAndTooLarge()
    {
        var commit = _github.Push(Repository, Descriptor, "{ }");
        _github.Push(Repository, "big.bin", new byte[11]);
        var reader = Reader();

        for (var pass = 0; pass < 2; pass++)
        {
            (await reader.ReadAsync(Repository, commit, Descriptor, 10, CancellationToken.None)).Outcome.Should().Be(FileRead.Found);
            (await reader.ReadAsync(Repository, commit, "absent.yml", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.NotFound);
        }

        var latest = (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None))!;
        (await reader.ReadAsync(Repository, latest, "big.bin", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.TooLarge);
        (await reader.ReadAsync(Repository, latest, "big.bin", 10, CancellationToken.None)).Content.Should().BeNull();

        _github.FileReads.Should().Equal(
            $"{Repository}@{commit}:{Descriptor}", $"{Repository}@{commit}:absent.yml", $"{Repository}@{latest}:big.bin");
    }

    [Fact]
    public async Task ReadAsync_ShouldReadAgain_WhenGitHubGaveNoVerdict()
    {
        var commit = _github.Push(Repository, Descriptor, FileRead.Unauthorized);
        var reader = Reader();

        (await reader.ReadAsync(Repository, commit, Descriptor, 100, CancellationToken.None)).Outcome.Should().Be(FileRead.Unauthorized);
        _github.Push(Repository, Descriptor, "{ }", commit: commit);
        var again = await reader.ReadAsync(Repository, commit, Descriptor, 100, CancellationToken.None);

        again.Outcome.Should().Be(FileRead.Found);
        Encoding.UTF8.GetString(again.Content!).Should().Be("{ }");
        _github.FileReads.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadTheNewCommit_WhenTheBranchMovedOn()
    {
        var first = _github.Push(Repository, Descriptor, "one");
        var second = _github.Push(Repository, Descriptor, "two");
        var reader = Reader();

        var before = await reader.ReadAsync(Repository, first, Descriptor, 100, CancellationToken.None);
        var after = await reader.ReadAsync(Repository, second, Descriptor, 100, CancellationToken.None);

        (Encoding.UTF8.GetString(before.Content!), Encoding.UTF8.GetString(after.Content!)).Should().Be(("one", "two"));
    }

    [Fact]
    public async Task HeadAsync_ShouldAskOnce_WithinTheHoldingTime_AndReadTheMovedBranchOnceEvicted()
    {
        var first = _github.Push(Repository, Descriptor, "one");
        var reader = Reader();

        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(first);
        var second = _github.Push(Repository, Descriptor, "two");
        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(first);
        _github.HeadReads.Should().ContainSingle();

        // The head entry lives HeadFor; removing it stands in for that expiry.
        await Cache.RemoveAsync(GitHubRepositoryReader.HeadKey(Repository, "main"));
        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(second);
        new GitHubRepositoryReaderOptions().HeadFor.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task HeadAsync_ShouldReplaceTheKeptHead_WhenFresh_AndKeepItWhenTheFreshReadFails()
    {
        _github.Push(Repository, Descriptor, "one");
        var reader = Reader();
        await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        var second = _github.Push(Repository, Descriptor, "two");

        (await reader.HeadAsync(Repository, "main", fresh: true, CancellationToken.None)).Should().Be(second);
        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(second);

        _github.Unreachable(Repository);
        (await reader.HeadAsync(Repository, "main", fresh: true, CancellationToken.None)).Should().BeNull();
        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(second);
        _github.HeadReads.Should().HaveCount(3);
    }

    [Fact]
    public async Task HeadAsync_ShouldKeepAnUnreadableBranch_AsLongAsAReadableOne()
    {
        var reader = Reader();

        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().BeNull();
        _github.Push(Repository, Descriptor, "one");
        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().BeNull();

        _github.HeadReads.Should().ContainSingle();
    }

    [Fact]
    public async Task HeadAsync_ShouldReadEveryTime_WhenHoldingIsOff()
    {
        var reader = Reader(o => o.HeadFor = TimeSpan.Zero);
        _github.Push(Repository, Descriptor, "one");
        await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None);
        var second = _github.Push(Repository, Descriptor, "two");

        (await reader.HeadAsync(Repository, "main", fresh: false, CancellationToken.None)).Should().Be(second);
        _github.HeadReads.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_ShouldKeepTheChosenBlobsPerPurpose_AndRetryAMissingTree()
    {
        _github.Push(Repository, "icons/icon.svg", "<svg/>");
        _github.Push(Repository, "icons/huge.png", new byte[50]);
        var commit = _github.Push(Repository, "readme.md", "# pilot");
        var reader = Reader();

        var icons = await reader.ListAsync(Repository, commit, "icons", (path, size) => path.StartsWith("icons/", StringComparison.Ordinal) && size < 20, CancellationToken.None);
        var again = await reader.ListAsync(Repository, commit, "icons", (_, _) => true, CancellationToken.None);
        var all = await reader.ListAsync(Repository, commit, "all", (_, _) => true, CancellationToken.None);
        var missing = await reader.ListAsync(Repository, new string('f', 40), "icons", (_, _) => true, CancellationToken.None);
        await reader.ListAsync(Repository, new string('f', 40), "icons", (_, _) => true, CancellationToken.None);

        icons.Should().Equal("icons/icon.svg");
        again.Should().Equal("icons/icon.svg");
        all.Should().Equal("icons/huge.png", "icons/icon.svg", "readme.md");
        missing.Should().BeNull();
        _github.TreeReads.Should().HaveCount(4);
    }

    [Fact]
    public void AddGitHubRepositoryReader_ShouldResolveTheReader_WithTheHostSectionAppliedLast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Integrations:GitHub:Reader:HeadFor"] = "00:00:30" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCaching();
        services.AddSingleton<IGitHubClient>(_github);
        services.AddGitHubRepositoryReader(o => o.HeadFor = TimeSpan.FromMinutes(1));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGitHubRepositoryReader>().Should().BeOfType<GitHubRepositoryReader>();
        provider.GetRequiredService<GitHubRepositoryReaderOptions>().HeadFor.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddGitHubRepositoryReader_ShouldRefuseANegativeHeadTime()
    {
        var services = new ServiceCollection();
        services.AddGitHubRepositoryReader(o => o.HeadFor = TimeSpan.FromSeconds(-1));
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<GitHubRepositoryReaderOptions>();

        resolve.Should().Throw<OptionsValidationException>();
    }
}
