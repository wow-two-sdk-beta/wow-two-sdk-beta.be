namespace WoW.Two.Sdk.Beta.Be.Foundation.Identifiers;

/// <summary>Holds URL-safe identifier alphabets.</summary>
public static class IdAlphabetConstants
{
    /// <summary>Holds the case-sensitive ASCII alphanumeric alphabet.</summary>
    public const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Holds the lowercase hexadecimal alphabet: four bits a character, so 64 characters carry 256 bits.</summary>
    public const string HexLower = "0123456789abcdef";

    /// <summary>Holds the base64url alphabet (RFC 4648 § 5): six bits a character, safe in URLs, headers and file names.</summary>
    public const string Base64Url = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
}
