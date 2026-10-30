using System.Globalization;
using System.Security.Cryptography;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>
/// Integrates a file as the master-key source: one line of base64 holding 256 bits (<c>openssl rand -base64 32</c>). Each read
/// takes the file as it is now — placed after start, it unseals without a restart; removed, the key is gone — and
/// returns a fresh copy the caller clears. The stamp is the file's last write time and size.
/// </summary>
/// <remarks>
///   - a file over 1 KiB, empty, or not decoding to exactly 32 bytes reads as <see cref="MasterKeyRead.Invalid"/>
///   - a file the process may not read reads as <see cref="MasterKeyRead.Missing"/>; <see cref="CheckAccess"/> tells
///     <see cref="MasterKeyRead.Unreadable"/> apart
///   - <see cref="TryLoadKey"/> throws <see cref="MasterKeyFormatException"/> for an invalid file, and a missing one
///     leaves <see cref="ISealService"/> sealed
/// </remarks>
/// <param name="path">The file's absolute path.</param>
public sealed class FileMasterKeyBroker(string path) : IMasterKeyBroker
{
    /// <summary>The largest file read as a key; a base64 key line is 44 characters.</summary>
    private const int MaxFileBytes = 1024;

    /// <summary>The mode bits that let anyone but the file's owner in.</summary>
    private const UnixFileMode GroupOrOthers = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <inheritdoc />
    public string Location => path;

    /// <inheritdoc />
    /// <remarks>Opens the file without reading it and reads the mode off the open handle. Nothing in .NET reads a file's
    /// owner, so the owner follows from the rest: the process opened a file only its owner may open. Windows has no
    /// mode bits, so an openable file passes there.</remarks>
    public string? CheckAccess()
    {
        try
        {
            if (!File.Exists(path))
                return MasterKeyRead.Missing;
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (OperatingSystem.IsWindows())
                return null;
            return (File.GetUnixFileMode(handle) & GroupOrOthers) != 0 ? MasterKeyRead.Exposed : null;
        }
        catch (FileNotFoundException)
        {
            return MasterKeyRead.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return MasterKeyRead.Missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MasterKeyRead.Unreadable;
        }
    }

    /// <inheritdoc />
    public MasterKeyRead Read()
    {
        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
                return new MasterKeyRead { Problem = MasterKeyRead.Missing, Stamp = "" };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new MasterKeyRead { Problem = MasterKeyRead.Missing, Stamp = "" };
        }

        var stamp = file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":"
            + file.Length.ToString(CultureInfo.InvariantCulture);
        if (file.Length is 0 or > MaxFileBytes)
            return new MasterKeyRead { Problem = MasterKeyRead.Invalid, Stamp = stamp };

        char[]? text = null;
        try
        {
            text = File.ReadAllText(path).Trim().ToCharArray();
            var key = new byte[64];
            if (!Convert.TryFromBase64Chars(text, key, out var written) || written != KeySizeConstants.SymmetricKeyBytes)
            {
                CryptographicOperations.ZeroMemory(key);
                return new MasterKeyRead { Problem = MasterKeyRead.Invalid, Stamp = stamp };
            }

            var exact = key[..KeySizeConstants.SymmetricKeyBytes];
            CryptographicOperations.ZeroMemory(key);
            return new MasterKeyRead { Key = exact, Stamp = stamp };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file the process may not read is as good as missing: the owner fixes its mode or owner.
            return new MasterKeyRead { Problem = MasterKeyRead.Missing, Stamp = stamp };
        }
        finally
        {
            if (text is not null)
                Array.Clear(text);
        }
    }

    /// <inheritdoc />
    /// <exception cref="MasterKeyFormatException">The file holds no base64 256-bit key.</exception>
    public byte[]? TryLoadKey()
    {
        var read = Read();
        if (read.Key is { } key)
            return key;
        if (read.Problem == MasterKeyRead.Invalid)
            throw new MasterKeyFormatException($"The master key file '{path}' holds no base64 256-bit key.");
        return null;
    }
}
