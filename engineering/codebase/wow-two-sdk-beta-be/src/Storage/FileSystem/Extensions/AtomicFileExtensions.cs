using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Storage.FileSystem.Extensions;

/// <summary>Extends local file storage with atomic writes: the content goes to a temporary file beside the target, which then
/// replaces the target in one rename, so a reader sees the old file or the new one and never half of one.</summary>
/// <remarks>
///   - a failed or cancelled write deletes its temporary file and leaves the target untouched
///   - concurrent writers each finish a whole file; the last rename wins, so serialize writers when order matters
///   - the temporary file is hidden (<c>.{name}.{32 hex}.partial</c>); <see cref="IsTemporaryFile"/> lets a folder scan skip one
///     a crash left behind
/// </remarks>
public static partial class AtomicFileExtensions
{
    /// <summary>Holds the suffix of a temporary file an atomic write creates.</summary>
    public const string TemporarySuffix = ".partial";

    private const int MaxNameLength = 200;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes a file atomically from <paramref name="write"/>, which receives the temporary file's stream.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="write">Writes the content; the stream is flushed and closed after it returns.</param>
    /// <param name="options">The permissions, folder creation and durability; <see cref="AtomicFileWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the target stays as it was.</param>
    /// <returns>A task completing once the new content has replaced the target.</returns>
    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        options ??= AtomicFileWriteOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target)
            ?? throw new ArgumentException("The path names no file.", nameof(path));
        if (options.CreateDirectory && !Directory.Exists(directory))
        {
            CreateDirectory(directory, options.DirectoryUnixCreateMode);
        }

        UnixFileMode? mode = PermissionsFor(target, options);
        string temporary = TemporaryPathFor(target);
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows() && mode is { } createMode)
        {
            streamOptions.UnixCreateMode = createMode;
        }

        try
        {
            FileStream stream = new(temporary, streamOptions);
            await using (stream.ConfigureAwait(false))
            {
                if (!OperatingSystem.IsWindows() && mode is { } exactMode)
                {
                    // The create mode passes through the umask; this sets the exact permissions before any content lands.
                    File.SetUnixFileMode(stream.SafeFileHandle, exactMode);
                }

                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (options.FlushToDisk)
                {
                    stream.Flush(flushToDisk: true);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Writes bytes to a file atomically.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The file's new content.</param>
    /// <param name="options">The permissions, folder creation and durability; <see cref="AtomicFileWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the target stays as it was.</param>
    /// <returns>A task completing once the new content has replaced the target.</returns>
    public static Task WriteAllBytesAsync(
        string path,
        ReadOnlyMemory<byte> content,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(path, (stream, ct) => stream.WriteAsync(content, ct).AsTask(), options, cancellationToken);

    /// <summary>Writes text to a file atomically, as UTF-8 without a byte-order mark.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The file's new content.</param>
    /// <param name="options">The permissions, folder creation and durability; <see cref="AtomicFileWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the target stays as it was.</param>
    /// <returns>A task completing once the new content has replaced the target.</returns>
    public static Task WriteAllTextAsync(
        string path,
        string content,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteAllBytesAsync(path, Utf8.GetBytes(content), options, cancellationToken);
    }

    /// <summary>Serializes <paramref name="value"/> to a file atomically.</summary>
    /// <typeparam name="T">The serialized type.</typeparam>
    /// <param name="path">The target file.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="json">The serializer options; the serializer's defaults when <see langword="null"/>.</param>
    /// <param name="options">The permissions, folder creation and durability; <see cref="AtomicFileWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the target stays as it was.</param>
    /// <returns>A task completing once the new content has replaced the target.</returns>
    public static Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonSerializerOptions? json = null,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(path, (stream, ct) => JsonSerializer.SerializeAsync(stream, value, json, ct), options, cancellationToken);

    /// <summary>Copies <paramref name="source"/> into a file atomically.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="source">The content, read from its current position to its end.</param>
    /// <param name="options">The permissions, folder creation and durability; <see cref="AtomicFileWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the target stays as it was.</param>
    /// <returns>A task completing once the new content has replaced the target.</returns>
    public static Task CopyToFileAsync(
        this Stream source,
        string path,
        AtomicFileWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return WriteAsync(path, (stream, ct) => source.CopyToAsync(stream, ct), options, cancellationToken);
    }

    /// <summary>Returns whether <paramref name="path"/> names a temporary file an atomic write created, such as one a crash left behind.</summary>
    /// <param name="path">A file path or name.</param>
    /// <returns><see langword="true"/> for <c>.{name}.{32 hex}.partial</c>.</returns>
    public static bool IsTemporaryFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return TemporaryName().IsMatch(Path.GetFileName(path));
    }

    private static string TemporaryPathFor(string target)
    {
        string name = Path.GetFileName(target);
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        return Path.Combine(Path.GetDirectoryName(target)!, $".{name}.{Guid.NewGuid():N}{TemporarySuffix}");
    }

    private static UnixFileMode? PermissionsFor(string target, AtomicFileWriteOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        if (options.UnixCreateMode is { } mode)
        {
            return mode;
        }

        try
        {
            return File.Exists(target) ? File.GetUnixFileMode(target) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void CreateDirectory(string directory, UnixFileMode? mode)
    {
        if (!OperatingSystem.IsWindows() && mode is { } unixMode)
        {
            Directory.CreateDirectory(directory, unixMode);
        }
        else
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void TryDelete(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The write already failed; IsTemporaryFile lets a later scan skip what remains.
        }
    }

    [GeneratedRegex(@"^\..+\.[0-9a-f]{32}\.partial$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryName();
}
