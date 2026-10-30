using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Integrations;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Integrations.GitHub;

/// <summary>Verifies the GitHub fake's repository reads: head, tree from the listed files, bounded file reads, and pushes to
/// several repositories and branches.</summary>
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

    [Fact]
    public async Task Push_ShouldMoveTheBranchToANewCommit_ThatKeepsTheBranchsOtherFiles()
    {
        var fake = new FakeGitHubClient();

        var first = fake.Push("o/a", "catalog.yml", "one");
        var second = fake.Push("o/a", "deploy.yml", "two");
        var head = await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None);

        first.Should().MatchRegex("^[0-9a-f]{40}$").And.NotBe(second);
        head.CommitSha.Should().Be(second);
        (await fake.ReadFileAsync("o/a", first, "deploy.yml", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.NotFound);
        Encoding.UTF8.GetString((await fake.ReadFileAsync("o/a", second, "catalog.yml", 10, CancellationToken.None)).Content!)
            .Should().Be("one");
        (await fake.GetTreeAsync("o/a", second, CancellationToken.None)).Entries.Select(entry => entry.Path)
            .Should().Equal("catalog.yml", "deploy.yml");
    }

    [Fact]
    public async Task Push_ShouldKeepRepositoriesAndBranchesApart_AndLeaveUnpushedOnesCanned()
    {
        var fake = new FakeGitHubClient().WithRepositoryFile("canned.txt", "c");

        var a = fake.Push("o/a", "f.txt", "a");
        var b = fake.Push("O/B", "f.txt", "b", branch: "release");

        (await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None)).CommitSha.Should().Be(a);
        (await fake.GetBranchHeadAsync("o/b", "release", CancellationToken.None)).CommitSha.Should().Be(b);
        (await fake.GetBranchHeadAsync("o/b", "main", CancellationToken.None)).Should().Be(BranchHead.Missing(RepositoryLookup.NotFound));
        (await fake.ReadFileAsync("o/a", b, "f.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.NotFound);
        (await fake.GetBranchHeadAsync("o/other", "main", CancellationToken.None)).CommitSha.Should().Be(FakeGitHubClient.DefaultCommitSha);
        (await fake.ReadFileAsync("o/other", "any", "canned.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.Found);

        fake.WithPushedOnly();
        (await fake.GetBranchHeadAsync("o/other", "main", CancellationToken.None)).Outcome.Should().Be(RepositoryLookup.NotFound);
        (await fake.GetTreeAsync("o/other", "any", CancellationToken.None)).Outcome.Should().Be(RepositoryLookup.NotFound);
        (await fake.ReadFileAsync("o/other", "any", "canned.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.NotFound);
    }

    [Fact]
    public async Task Push_ShouldAddToANamedCommit_AndScriptAFileOutcome()
    {
        var fake = new FakeGitHubClient();
        var commit = new string('a', 40);

        fake.Push("o/a", "x.txt", "x", commit: commit).Should().Be(commit);
        fake.Push("o/a", "big.bin", FileRead.TooLarge, commit: commit);
        fake.Push("o/a", "secret.txt", FileRead.Unauthorized, commit: commit);

        (await fake.ReadFileAsync("o/a", commit, "x.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.Found);
        (await fake.ReadFileAsync("o/a", commit, "x.txt", 0, CancellationToken.None)).Outcome.Should().Be(FileRead.TooLarge);
        (await fake.ReadFileAsync("o/a", commit, "big.bin", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.TooLarge);
        (await fake.ReadFileAsync("o/a", commit, "secret.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.Unauthorized);
        (await fake.GetTreeAsync("o/a", commit, CancellationToken.None)).Entries
            .Select(entry => (entry.Path, entry.Size)).Should().Equal(("big.bin", null), ("secret.txt", null), ("x.txt", 1L));

        var removed = fake.Push("o/a", "x.txt", FileRead.NotFound);
        (await fake.ReadFileAsync("o/a", removed, "x.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.NotFound);
        var push = () => fake.Push("o/a", "y.txt", FileRead.Found);
        push.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Unreachable_ShouldFailTheHead_UntilTheNextPush_WhichStartsFromNoFiles()
    {
        var fake = new FakeGitHubClient();
        var before = fake.Push("o/a", "a.txt", "a");

        fake.Unreachable("o/a", outcome: RepositoryLookup.Unauthorized);
        (await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None)).Should().Be(BranchHead.Missing(RepositoryLookup.Unauthorized));
        (await fake.ReadFileAsync("o/a", before, "a.txt", 10, CancellationToken.None)).Outcome.Should().Be(FileRead.Found);

        var after = fake.Push("o/a", "b.txt", "b");
        (await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None)).CommitSha.Should().Be(after);
        (await fake.GetTreeAsync("o/a", after, CancellationToken.None)).Entries.Select(entry => entry.Path).Should().Equal("b.txt");

        var empty = fake.PushEmpty("o/a");
        (await fake.GetTreeAsync("o/a", empty, CancellationToken.None)).Should().BeEquivalentTo(RepositoryTree.Empty(RepositoryLookup.Found));
    }

    [Fact]
    public async Task Reads_ShouldBeRecorded_ThrowTheScriptedFailure_AndClear()
    {
        var fake = new FakeGitHubClient();
        var commit = fake.Push("o/a", "a.txt", "a");

        await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None);
        await fake.GetTreeAsync("o/a", commit, CancellationToken.None);
        await fake.ReadFileAsync("o/a", commit, "a.txt", 10, CancellationToken.None);
        fake.Failure = new InvalidOperationException("down");
        var read = () => fake.ReadFileAsync("o/a", commit, "b.txt", 10, CancellationToken.None);

        await read.Should().ThrowAsync<InvalidOperationException>();
        fake.HeadReads.Should().Equal("o/a@main");
        fake.TreeReads.Should().Equal($"o/a@{commit}");
        fake.FileReads.Should().Equal($"o/a@{commit}:a.txt", $"o/a@{commit}:b.txt");

        fake.Failure = null;
        fake.Clear();
        fake.FileReads.Should().BeEmpty();
        (await fake.GetBranchHeadAsync("o/a", "main", CancellationToken.None)).CommitSha.Should().Be(FakeGitHubClient.DefaultCommitSha);
    }
}
