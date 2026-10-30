using System.Security.Cryptography;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Security;

/// <summary>Integrates the process environment as the master-key source — a base64-encoded 256-bit key read from one variable.</summary>
/// <remarks>The env-var name comes from <see cref="EnvelopeCryptographyOptions.MasterKeyEnvironmentVariable"/>. Suitable for containers and local development; swap for a KMS/HSM-backed broker in production by registering your own <see cref="IMasterKeyBroker"/> before <c>AddEnvelopeCryptography</c>. Never logs the key value.</remarks>
internal sealed class EnvironmentMasterKeyBroker(EnvelopeCryptographyOptions options) : IMasterKeyBroker
{
    private readonly EnvelopeCryptographyOptions _options = options;

    /// <inheritdoc />
    public string Location => "env:" + _options.MasterKeyEnvironmentVariable;

    /// <inheritdoc />
    /// <remarks>The stamp is 16 hex digits of a labelled SHA-256 of the variable's value: it changes with the value and
    /// tells nothing usable about a 256-bit key.</remarks>
    public MasterKeyRead Read()
    {
        var raw = Environment.GetEnvironmentVariable(_options.MasterKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return new MasterKeyRead { Problem = MasterKeyRead.Missing, Stamp = "" };

        var text = raw.Trim();
        var stamp = Stamp(text);
        var key = new byte[64];
        if (!Convert.TryFromBase64String(text, key, out var written) || written != KeySizeConstants.SymmetricKeyBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            return new MasterKeyRead { Problem = MasterKeyRead.Invalid, Stamp = stamp };
        }

        var exact = key[..KeySizeConstants.SymmetricKeyBytes];
        CryptographicOperations.ZeroMemory(key);
        return new MasterKeyRead { Key = exact, Stamp = stamp };
    }

    /// <inheritdoc />
    public string? CheckAccess() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_options.MasterKeyEnvironmentVariable)) ? MasterKeyRead.Missing : null;

    /// <inheritdoc />
    public byte[]? TryLoadKey()
    {
        var envVar = _options.MasterKeyEnvironmentVariable;
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(raw);
        }
        catch (FormatException ex)
        {
            // Never surface the raw value — only the env-var name, which is not secret.
            throw new MasterKeyFormatException($"Master key in '{envVar}' is not valid base64.", ex);
        }

        if (key.Length != KeySizeConstants.SymmetricKeyBytes)
        {
            // Clear the wrong-sized buffer before throwing — it may still hold partial key material.
            CryptographicOperations.ZeroMemory(key);
            throw new MasterKeyFormatException(
                $"Master key in '{envVar}' must decode to {KeySizeConstants.SymmetricKeyBytes} bytes.");
        }

        return key;
    }

    /// <summary>A content stamp that changes with <paramref name="text"/> without revealing it.</summary>
    private static string Stamp(string text)
    {
        var input = Encoding.UTF8.GetBytes("envelope-cryptography.env-stamp\0" + text);
        try
        {
            return "env:" + Convert.ToHexStringLower(SHA256.HashData(input))[..16];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
