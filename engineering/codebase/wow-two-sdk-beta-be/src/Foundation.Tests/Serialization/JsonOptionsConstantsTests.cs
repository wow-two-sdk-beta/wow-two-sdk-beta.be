using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Serialization;

/// <summary>Covers the wire preset's naming: camelCase properties, dictionary keys as written.</summary>
public sealed class JsonOptionsConstantsTests
{
    private sealed record Stock(IReadOnlyDictionary<string, int> BySize);

    [Fact]
    public void Default_ShouldKeepDictionaryKeysAsWritten_WhenTheyCarryData()
    {
        var stock = new Stock(new Dictionary<string, int> { ["XL"] = 2, ["SizeM"] = 1 });

        var json = JsonSerializer.Serialize(stock, JsonOptionsConstants.Default);

        json.Should().Be("""{"bySize":{"XL":2,"SizeM":1}}""");
    }

    [Fact]
    public void Default_ShouldReadDictionaryKeysBack_WhenRoundTripped()
    {
        var stock = new Stock(new Dictionary<string, int> { ["XL"] = 2 });

        var read = JsonSerializer.Deserialize<Stock>(JsonSerializer.Serialize(stock, JsonOptionsConstants.Default), JsonOptionsConstants.Default);

        read!.BySize.Should().ContainKey("XL");
    }
}
