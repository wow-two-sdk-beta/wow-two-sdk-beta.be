using System.Security.Cryptography;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>
/// Unseals by reading the master key on every use and proves each new key content once: the caller's proof opens something only
/// the right key opens — a <see cref="KeyCanary"/>, else a wrapped data key — and the stamp it accepted is remembered,
/// so later reads skip the proof until the source changes. Holds no key between calls.
/// </summary>
/// <remarks>
///   - a key that fails its proof is cleared and reads as <see cref="MasterKeyRead.Mismatch"/>; the proof is forgotten
///   - a proof that throws <see cref="CryptographicException"/> fails; any other exception clears the key and propagates
///   - register as a singleton beside one <see cref="MasterKeyProofTracker"/>; <c>AddFileMasterKeyBroker</c> does both
/// </remarks>
/// <param name="broker">Where the key is read from.</param>
/// <param name="proofs">Which key content a proof accepted.</param>
public sealed class MasterKeyUnsealService(IMasterKeyBroker broker, MasterKeyProofTracker proofs)
{
    /// <summary>Reads the key and, unless its content was proven before, proves it with <paramref name="prove"/>.</summary>
    /// <param name="prove">Whether the key opens what the caller holds; an empty store accepts any key until its first
    /// write binds one.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The read with the key, which the caller clears after use; or why there is none, <see cref="MasterKeyRead.Mismatch"/>
    /// when the proof failed.</returns>
    public async Task<MasterKeyRead> UnsealAsync(Func<byte[], CancellationToken, Task<bool>> prove, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prove);

        var read = broker.Read();
        if (read.Key is not { } key)
            return read.Problem is null ? read with { Problem = MasterKeyRead.Missing } : read;
        if (proofs.IsProven(read.Stamp))
            return read;

        bool proven;
        try
        {
            proven = await prove(key, ct).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            proven = false;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }

        if (proven)
        {
            proofs.Prove(read.Stamp);
            return read;
        }

        CryptographicOperations.ZeroMemory(key);
        proofs.Forget();
        return new MasterKeyRead { Problem = MasterKeyRead.Mismatch, Stamp = read.Stamp };
    }
}
