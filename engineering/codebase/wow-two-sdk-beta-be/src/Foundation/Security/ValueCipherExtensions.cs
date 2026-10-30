using System.Security.Cryptography;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Extends <see cref="IValueCipher"/> with text values and the master-key canary; every plaintext byte buffer is
/// cleared after use.</summary>
public static class ValueCipherExtensions
{
    /// <summary>Encrypts a text value as UTF-8 under <paramref name="dataKey"/>, bound to <paramref name="associatedData"/>.</summary>
    /// <param name="cipher">The value cipher.</param>
    /// <param name="value">The text to protect.</param>
    /// <param name="dataKey">The 256-bit data key.</param>
    /// <param name="associatedData">The context the value is bound to.</param>
    /// <returns>The ciphertext with its nonce and tag.</returns>
    public static EncryptedPayload EncryptText(this IValueCipher cipher, string value, byte[] dataKey, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(value);

        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return cipher.Encrypt(plaintext, dataKey, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Decrypts a text value <see cref="EncryptText"/> sealed.</summary>
    /// <param name="cipher">The value cipher.</param>
    /// <param name="payload">The ciphertext with its nonce and tag.</param>
    /// <param name="dataKey">The data key it was sealed under.</param>
    /// <param name="associatedData">The context it was bound to.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CryptographicException">The key, context or bytes do not verify.</exception>
    public static string DecryptText(this IValueCipher cipher, EncryptedPayload payload, byte[] dataKey, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        var plaintext = cipher.Decrypt(payload, dataKey, associatedData);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Makes a <see cref="KeyCanary"/> under <paramref name="masterKey"/>: a random canary key wrapped under it with
    /// <paramref name="keyContext"/>, a random value sealed under the canary key with <paramref name="valueContext"/>.</summary>
    /// <param name="cipher">The value cipher.</param>
    /// <param name="masterKey">The master key to prove later; the caller still clears it.</param>
    /// <param name="keyContext">The associated data of the wrapped canary key.</param>
    /// <param name="valueContext">The associated data of the canary value.</param>
    /// <returns>The canary to store beside the data.</returns>
    public static KeyCanary CreateCanary(this IValueCipher cipher, byte[] masterKey, string keyContext, string valueContext)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentException.ThrowIfNullOrEmpty(keyContext);
        ArgumentException.ThrowIfNullOrEmpty(valueContext);

        var key = RandomNumberGenerator.GetBytes(KeySizeConstants.SymmetricKeyBytes);
        var value = RandomNumberGenerator.GetBytes(KeySizeConstants.SymmetricKeyBytes);
        try
        {
            return new KeyCanary
            {
                WrappedKey = cipher.Encrypt(key, masterKey, keyContext),
                Value = cipher.Encrypt(value, key, valueContext),
                Digest = SHA256.HashData(value),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(value);
        }
    }

    /// <summary>Whether <paramref name="canary"/> opens under <paramref name="masterKey"/> to its digest.</summary>
    /// <param name="cipher">The value cipher.</param>
    /// <param name="canary">The stored canary.</param>
    /// <param name="masterKey">The master key to prove; the caller still clears it.</param>
    /// <param name="keyContext">The associated data the canary key was wrapped with.</param>
    /// <param name="valueContext">The associated data the canary value was sealed with.</param>
    /// <returns><see langword="true"/> when the key opens the canary; <see langword="false"/> for another key, other
    /// contexts or changed bytes.</returns>
    public static bool VerifyCanary(
        this IValueCipher cipher, KeyCanary canary, byte[] masterKey, string keyContext, string valueContext)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(canary);
        ArgumentNullException.ThrowIfNull(masterKey);

        byte[]? key = null;
        byte[]? value = null;
        try
        {
            key = cipher.Decrypt(canary.WrappedKey, masterKey, keyContext);
            value = cipher.Decrypt(canary.Value, key, valueContext);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(value), canary.Digest);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            if (key is not null)
                CryptographicOperations.ZeroMemory(key);
            if (value is not null)
                CryptographicOperations.ZeroMemory(value);
        }
    }
}
