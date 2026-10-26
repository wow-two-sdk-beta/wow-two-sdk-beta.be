using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Storage.FileSystem;
using WoW.Two.Sdk.Beta.Be.Storage.FileSystem.Extensions;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Storage;

/// <summary>Atomic file writes: a reader sees the old file or the new one, a failed write leaves the target and no debris,
/// and permissions hold from creation.</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"atomic-{Guid.NewGuid():N}");

    [Fact]
    public async Task Write_ReplacesTheFileAndLeavesNoTemporaryFile()
    {
        string path = Path.Combine(_root, "nested", "inventory.json");
        await AtomicFileExtensions.WriteAllTextAsync(path, "first");
        await AtomicFileExtensions.WriteAllTextAsync(path, "second");

        (await File.ReadAllTextAsync(path)).Should().Be("second");
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().Equal(path);
    }

    [Fact]
    public async Task FailedWrite_LeavesTheTargetAndNoDebris()
    {
        string path = Path.Combine(_root, "tokens.json");
        await AtomicFileExtensions.WriteAllTextAsync(path, "intact");

        var act = () => AtomicFileExtensions.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync("half"u8.ToArray(), ct);
            throw new IOException("disk gone");
        });

        await act.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(path)).Should().Be("intact");
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task CancelledWrite_LeavesTheTarget()
    {
        string path = Path.Combine(_root, "state.json");
        await AtomicFileExtensions.WriteAllTextAsync(path, "intact");
        using var cancel = new CancellationTokenSource();

        var act = () => AtomicFileExtensions.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), ct);
            await cancel.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }, cancellationToken: cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(path)).Should().Be("intact");
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task ReaderDuringAWrite_SeesTheWholeOldFile()
    {
        string path = Path.Combine(_root, "snapshot.json");
        await AtomicFileExtensions.WriteAllTextAsync(path, "old");
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task writing = AtomicFileExtensions.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync("ne"u8.ToArray(), ct);
            await stream.FlushAsync(ct);
            halfway.SetResult();
            await resume.Task;
            await stream.WriteAsync("w"u8.ToArray(), ct);
        });

        await halfway.Task;
        (await File.ReadAllTextAsync(path)).Should().Be("old");
        resume.SetResult();
        await writing;
        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Fact]
    public async Task ConcurrentWriters_EachLeaveAWholeFile()
    {
        string path = Path.Combine(_root, "race.json");
        string[] contents = [.. Enumerable.Range(0, 16).Select(writer => new string((char)('a' + writer), 64 * 1024))];

        await Task.WhenAll(contents.Select(content => Task.Run(() => AtomicFileExtensions.WriteAllTextAsync(path, content))));

        contents.Should().Contain(await File.ReadAllTextAsync(path));
        Directory.GetFiles(_root).Should().Equal(path);
    }

    [Fact]
    public async Task Json_RoundTrips()
    {
        string path = Path.Combine(_root, "model.json");
        await AtomicFileExtensions.WriteJsonAsync(path, new { Name = "wheelhouse", Count = 3 });

        (await File.ReadAllTextAsync(path)).Should().Be("""{"Name":"wheelhouse","Count":3}""");
    }

    [Fact]
    public async Task UnixCreateMode_IsExactAndAReplacementKeepsTheFilesMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Combine(_root, "secret.json");
        var owner = new AtomicFileWriteOptions
        {
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            DirectoryUnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
        };
        await AtomicFileExtensions.WriteAllTextAsync(path, "one", owner);
        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await AtomicFileExtensions.WriteAllTextAsync(path, "two");
        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, "a replacement keeps the file's mode");
    }

    [Fact]
    public async Task MissingFolder_IsRefusedWhenCreationIsOff()
    {
        string path = Path.Combine(_root, "absent", "file.txt");

        var act = () => AtomicFileExtensions.WriteAllTextAsync(path, "x", new AtomicFileWriteOptions { CreateDirectory = false });

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Theory]
    [InlineData(".inventory.json.0123456789abcdef0123456789abcdef.partial", true)]
    [InlineData("/var/data/.a.0123456789abcdef0123456789abcdef.partial", true)]
    [InlineData("inventory.json", false)]
    [InlineData(".inventory.json.partial", false)]
    [InlineData("notes.0123456789abcdef0123456789abcdef.partial", false)]
    public void IsTemporaryFile_MatchesOnlyAnAtomicWritesTemporaryName(string path, bool expected) =>
        AtomicFileExtensions.IsTemporaryFile(path).Should().Be(expected);

    [Fact]
    public async Task LocalBlobSave_ThatFailsLeavesNoPartialBlobAndListingsSkipCrashDebris()
    {
        var blobs = new LocalFileBlobRepository(_root);
        await blobs.SaveAsync("avatars/u1.png", new MemoryStream(Encoding.UTF8.GetBytes("whole")));

        var act = () => blobs.SaveAsync("avatars/u1.png", new FailingStream());
        await act.Should().ThrowAsync<IOException>();
        var fresh = () => blobs.SaveAsync("avatars/u2.png", new FailingStream());
        await fresh.Should().ThrowAsync<IOException>();

        // A crash mid-save would leave its temporary file; listings never show it.
        await File.WriteAllTextAsync(Path.Combine(_root, "avatars", ".u3.png.0123456789abcdef0123456789abcdef.partial"), "debris");
        var listed = new List<string>();
        await foreach (var blob in blobs.ListAsync("avatars/"))
        {
            listed.Add(blob.Path);
        }

        listed.Should().Equal("avatars/u1.png");
        (await blobs.ExistsAsync("avatars/u2.png")).Should().BeFalse();
        using var reader = new StreamReader((await blobs.OpenReadAsync("avatars/u1.png"))!);
        (await reader.ReadToEndAsync()).Should().Be("whole");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        private bool _served;

        public FailingStream()
            : base(Encoding.UTF8.GetBytes("partial"))
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
            {
                throw new IOException("upload dropped");
            }

            _served = true;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
