using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Integrations.GitHub;

/// <summary>Verifies the GitHub fake's repository reads: head, tree from the listed files, and bounded file reads.</summary>
public sealed class FakeGitHubClientTests
{
    [Fact]
    public async Task Defaults_ShouldReportAHeadAndAnEmptyTree()
    {
        var fake = new FakeGitHubClient();

        (await fake.GetBranchHeadAsync("o/r", "main", CancellationToken.None)).CommitSha
            .Should().Be(FakeGitHubClient.DefaultCommitSha);
        (await fake.GetTreeAsync("o/r", FakeGitHubClient.DefaultCommitSha, CancellationToken.None))
            .Should().BeEquivalentTo(RepositoryTree.Empty(RepositoryLookup.Found));
    }

    [Fact]
    public async Task RepositoryFiles_ShouldDriveTheTreeAndTheReads()
    {
        var fake = new FakeGitHubClient()
            .WithRepositoryFile("b.txt", "bbb")
            .WithRepositoryFile("a/icon.svg", "<svg/>");

        var tree = await fake.GetTreeAsync("o/r", "sha", CancellationToken.None);
        var found = await fake.ReadFileAsync("o/r", "sha", "b.txt", 10, CancellationToken.None);
        var tooLarge = await fake.ReadFileAsync("o/r", "sha", "a/icon.svg", 3, CancellationToken.None);
        var missing = await fake.ReadFileAsync("o/r", "sha", "c.txt", 10, CancellationToken.None);

        tree.Entries.Select(entry => (entry.Path, entry.Size)).Should().Equal(("a/icon.svg", 6L), ("b.txt", 3L));
        Encoding.UTF8.GetString(found.Content!).Should().Be("bbb");
        tooLarge.Outcome.Should().Be(FileRead.TooLarge);
        missing.Outcome.Should().Be(FileRead.NotFound);
    }

    [Fact]
    public async Task ForcedOutcomes_ShouldOverrideTheListedFiles()
    {
        var fake = new FakeGitHubClient()
            .WithRepositoryFile("a.txt", "a")
            .WithHeadOutcome(RepositoryLookup.Unauthorized)
            .WithTreeOutcome(RepositoryLookup.Failed)
            .WithFileReadOutcome(FileRead.Unauthorized);

        (await fake.GetBranchHeadAsync("o/r", "main", CancellationToken.None))
            .Should().Be(BranchHead.Missing(RepositoryLookup.Unauthorized));
        (await fake.GetTreeAsync("o/r", "sha", CancellationToken.None)).Outcome.Should().Be(RepositoryLookup.Failed);
        (await fake.ReadFileAsync("o/r", "sha", "a.txt", 10, CancellationToken.None))
            .Should().Be(RepositoryFile.Missing(FileRead.Unauthorized));
    }
}
