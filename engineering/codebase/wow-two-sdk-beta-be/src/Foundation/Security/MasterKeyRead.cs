namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Represents one read of the master key from an <see cref="IMasterKeyBroker"/>: the key, or why there is none.</summary>
public sealed record MasterKeyRead
{
    /// <summary>The 32-byte key, a fresh copy the caller clears; <see langword="null"/> when there is none. Never keep or log it.</summary>
    public byte[]? Key { get; init; }

    /// <summary>Why there is no key — <see cref="Missing"/>, <see cref="Invalid"/>, <see cref="Unreadable"/>,
    /// <see cref="Exposed"/> or <see cref="Mismatch"/>; <see langword="null"/> when there is one.</summary>
    public string? Problem { get; init; }

    /// <summary>What identifies the key's source content without its value, such as a file's write time and size, so a
    /// check made once holds until the content changes; empty when there is no content.</summary>
    public required string Stamp { get; init; }

    /// <summary>The problem code of a key that is not there.</summary>
    public const string Missing = "missing";

    /// <summary>The problem code of a source that holds no base64 256-bit key.</summary>
    public const string Invalid = "invalid";

    /// <summary>The problem code of a key file the process's user may not open.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>The problem code of a key file whose mode lets its group or others in.</summary>
    public const string Exposed = "exposed";

    /// <summary>The problem code of a key that does not open what it is proven against — another store's key.</summary>
    public const string Mismatch = "mismatch";

    /// <summary>Returns the read without its key, so a log cannot print one.</summary>
    /// <returns>The read's state: its problem, or <c>present</c>.</returns>
    public override string ToString() => nameof(MasterKeyRead) + " { " + (Problem ?? "present") + " }";
}
