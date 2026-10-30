using System.Security.Cryptography;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>
/// Hashes a secret value into a fingerprint, so equal values show equal fingerprints without letting anyone test a
/// guess: 16 hex digits of an HMAC-SHA256 keyed by a key derived (HKDF-SHA256) from the data key the value is sealed under.
/// </summary>
/// <remarks>
///   - the purpose is the HKDF info label; one per scheme (<c>myapp-vault/fingerprint</c>), hashed as UTF-8
///   - a fingerprint compares only under one data key; another owner's equal value fingerprints differently
///   - stateless and thread-safe
/// </remarks>
/// <param name="purpose">The HKDF info label the HMAC key is derived with.</param>
public sealed class ValueFingerprintHasher(string purpose)
{
    /// <summary>How many hex digits a fingerprint keeps.</summary>
    public const int Digits = 16;

    private readonly byte[] _info = Encoding.UTF8.GetBytes(
        string.IsNullOrEmpty(purpose) ? throw new ArgumentException("A fingerprint needs a purpose.", nameof(purpose)) : purpose);

    /// <summary>Hashes a value into its fingerprint under its data key.</summary>
    /// <param name="value">The value.</param>
    /// <param name="dataKey">The data key the value is sealed under; the caller still clears it.</param>
    /// <returns>Sixteen lowercase hex digits.</returns>
    public string Hash(string value, byte[] dataKey)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(dataKey);

        var macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, dataKey, KeySizeConstants.SymmetricKeyBytes, info: _info);
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexStringLower(HMACSHA256.HashData(macKey, plaintext))[..Digits];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
