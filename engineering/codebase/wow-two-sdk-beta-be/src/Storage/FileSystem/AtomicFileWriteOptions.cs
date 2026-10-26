namespace WoW.Two.Sdk.Beta.Be.Storage.FileSystem;

/// <summary>Holds options for one atomic file write.</summary>
public sealed record AtomicFileWriteOptions
{
    /// <summary>Gets the default options: create the folder, keep a replaced file's permissions, flush to disk before the swap.</summary>
    public static AtomicFileWriteOptions Default { get; } = new();

    /// <summary>Gets the Unix permissions of the written file, applied from the moment the temporary file exists; ignored on Windows.
    /// <see langword="null"/> (the default) keeps the permissions of the file being replaced, or the process default for a new one.</summary>
    public UnixFileMode? UnixCreateMode { get; init; }

    /// <summary>Gets whether a missing folder is created. Defaults to <see langword="true"/>.</summary>
    public bool CreateDirectory { get; init; } = true;

    /// <summary>Gets the Unix permissions of a folder this write creates; ignored on Windows and for a folder that exists.
    /// <see langword="null"/> (the default) uses the process default.</summary>
    public UnixFileMode? DirectoryUnixCreateMode { get; init; }

    /// <summary>Gets whether the content is flushed to the disk before it replaces the target, so a power loss after the swap cannot
    /// leave an empty file. Defaults to <see langword="true"/>.</summary>
    public bool FlushToDisk { get; init; } = true;
}
