namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>
/// A known value sealed under a master key, kept beside the data that key protects, so a key read later is proven by
/// opening it: a random canary key wrapped under the master key, a random value sealed under the canary key, and the
/// value's SHA-256. Made by <see cref="ValueCipherExtensions.CreateCanary"/>, checked by <see cref="ValueCipherExtensions.VerifyCanary"/>.
/// </summary>
/// <remarks>Nothing here is secret: the digest is of random bytes, and both payloads need the master key to open.</remarks>
public sealed record KeyCanary
{
    /// <summary>The canary key, wrapped under the master key.</summary>
    public required EncryptedPayload WrappedKey { get; init; }

    /// <summary>The canary value, sealed under the canary key.</summary>
    public required EncryptedPayload Value { get; init; }

    /// <summary>The SHA-256 of the canary value's 32 bytes.</summary>
    public required byte[] Digest { get; init; }
}
