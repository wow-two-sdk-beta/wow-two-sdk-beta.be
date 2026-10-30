namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Defines behavior that integrates the master-key (KEK) source, read anew on every call with a stamp of the content read, so the source swaps without touching the crypto plane.</summary>
/// <remarks>
///   - an environment variable (<see cref="EnvironmentMasterKeyBroker"/>) or a file (<see cref="FileMasterKeyBroker"/>) today, a cloud KMS or HSM later
///   - two consumers: <see cref="ISealService"/> loads the key once through <see cref="TryLoadKey"/> and holds it;
///     <see cref="MasterKeyUnsealService"/> reads it per use through <see cref="Read"/> and holds nothing, so a key placed
///     after start unseals without a restart and a removed one seals again
///   - implementations treat the returned bytes as sensitive and never cache them; every read returns a fresh copy the caller clears
/// </remarks>
public interface IMasterKeyBroker
{
    /// <summary>Gets where the key is read from, as configured; safe to show, never the key.</summary>
    string Location { get; }

    /// <summary>Reads the key now; a missing or malformed key reads as the reason.</summary>
    /// <returns>The key, a fresh copy the caller clears, or why there is none; the stamp changes when the content does.</returns>
    MasterKeyRead Read();

    /// <summary>Checks where the key lives without reading it: present, readable, and closed to everyone but its owner.</summary>
    /// <returns><see cref="MasterKeyRead.Missing"/>, <see cref="MasterKeyRead.Unreadable"/> or <see cref="MasterKeyRead.Exposed"/>;
    /// <see langword="null"/> when the check passes.</returns>
    string? CheckAccess();

    /// <summary>Loads the raw master-key bytes, or <see langword="null"/> when no key is configured (the seal service then stays sealed rather than throwing).</summary>
    /// <returns>The 256-bit (32-byte) master key, or <see langword="null"/> when unavailable.</returns>
    /// <exception cref="MasterKeyFormatException">A key is configured but malformed — wrong encoding or wrong length.</exception>
    byte[]? TryLoadKey()
    {
        var read = Read();
        if (read.Key is { } key)
            return key;
        if (read.Problem == MasterKeyRead.Invalid)
            throw new MasterKeyFormatException($"The master key at '{Location}' holds no base64 256-bit key.");
        return null;
    }
}
