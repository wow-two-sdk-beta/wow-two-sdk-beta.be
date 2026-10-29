using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers.Generators;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Identifiers;

public sealed class CryptographicIdGeneratorTests
{
    private readonly CryptographicIdGenerator _generator = new CryptographicIdGenerator();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void Generate_UsesRequestedLengthAndAlphabet(int length)
    {
        var value = _generator.Generate(length, "01-_");
        Assert.Equal(length, value.Length);
        Assert.All(value, c => Assert.Contains(c, "01-_"));
    }

    [Theory]
    [InlineData(IdAlphabetConstants.Alphanumeric, 62)]
    [InlineData(IdAlphabetConstants.HexLower, 16)]
    [InlineData(IdAlphabetConstants.Base64Url, 64)]
    public void Alphabets_AreDistinctUrlSafeCharacters_TheGeneratorAccepts(string alphabet, int size)
    {
        Assert.Equal(size, alphabet.Distinct().Count());
        var value = _generator.Generate(256, alphabet);
        Assert.All(value, c => Assert.Contains(c, alphabet));
    }

    [Theory]
    [InlineData(0, "ab")]
    [InlineData(4097, "ab")]
    [InlineData(7, "a")]
    [InlineData(7, "aa")]
    [InlineData(7, "aé")]
    [InlineData(7, "a/")]
    public void Generate_RejectsInvalidConfiguration(int length, string alphabet)
        => Assert.ThrowsAny<ArgumentException>(() => _generator.Generate(length, alphabet));
}
