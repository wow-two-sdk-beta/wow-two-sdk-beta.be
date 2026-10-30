using System.Security.Cryptography;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>
/// Hashes a key into a fingerprint that names it without revealing it: the first 80 bits of SHA-256 over the label, a
/// zero byte and the key, in lowercase hex groups of four (<c>aeb1 80f7 9014 1a39 9680</c>). The label keeps the
/// fingerprint apart from any other hash of the same key, so an owner's offline copy and a running service print the
/// same text to compare by eye.
/// </summary>
/// <remarks>One label per key purpose and scheme (<c>myapp.vault.root-key.v1</c>); a new scheme takes a new label. The
/// label is hashed as UTF-8, which equals ASCII for an ASCII label. Stateless and thread-safe.</remarks>
/// <param name="label">The label hashed before every key.</param>
public sealed class KeyFingerprintHasher(string label)
{
    /// <summary>How many hex digits a fingerprint keeps.</summary>
    public const int Digits = 20;

    /// <summary>Matches a fingerprint as <see cref="Hash"/> writes it.</summary>
    public const string Pattern = "^[0-9a-f]{4}( [0-9a-f]{4}){4}$";

    private readonly byte[] _label = Encoding.UTF8.GetBytes(
        string.IsNullOrEmpty(label) ? throw new ArgumentException("A fingerprint needs a label.", nameof(label)) : label);

    /// <summary>Hashes a key into its fingerprint.</summary>
    /// <param name="key">The key; the caller still clears it.</param>
    /// <returns>Twenty hex digits in groups of four, space-separated.</returns>
    public string Hash(ReadOnlySpan<byte> key)
    {
        var input = new byte[_label.Length + 1 + key.Length];
        try
        {
            _label.CopyTo(input, 0);
            key.CopyTo(input.AsSpan(_label.Length + 1));
            var hex = Convert.ToHexStringLower(SHA256.HashData(input))[..Digits];
            return string.Join(' ', Enumerable.Range(0, Digits / 4).Select(group => hex.Substring(group * 4, 4)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
