namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Tracks, for the life of the process, which master key content a proof accepted — by its
/// <see cref="MasterKeyRead.Stamp"/> — so a read checks the stamp rather than proving the key each time. Holds no key;
/// register as a singleton.</summary>
public sealed class MasterKeyProofTracker
{
    private volatile string? _proven;

    /// <summary>Whether a proof accepted the key content with this stamp.</summary>
    /// <param name="stamp">The key read's stamp.</param>
    /// <returns><see langword="true"/> when proven.</returns>
    public bool IsProven(string stamp) => string.Equals(_proven, stamp, StringComparison.Ordinal);

    /// <summary>Records that a proof accepted the key content with this stamp.</summary>
    /// <param name="stamp">The key read's stamp.</param>
    public void Prove(string stamp) => _proven = stamp;

    /// <summary>Forgets any proof, so the next read proves the key again.</summary>
    public void Forget() => _proven = null;
}
