using System.Buffers.Binary;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit.Validators;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Audit;

/// <summary>Verifies that timestamp fields hash at microsecond precision, so a chain survives a PostgreSQL round trip.</summary>
public sealed class CanonicalPayloadBuilderTimestampTests
{
    private static readonly DateTimeOffset MicrosecondInstant =
        new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero).AddTicks(1_234_560);

    [Fact]
    public void Validate_AfterSubMicrosecondTicksAreTruncated_ShouldReportIntactChain()
    {
        using var provider = BuildProvider();
        var sealer = provider.GetRequiredService<IHashChainSealer<TimestampedEntry>>();
        var validator = provider.GetRequiredService<IHashChainValidator<TimestampedEntry>>();
        var first = new TimestampedEntry { OccurredAt = MicrosecondInstant.AddTicks(7) };
        var second = new TimestampedEntry { OccurredAt = MicrosecondInstant.AddTicks(19) };
        sealer.Seal(first, null);
        sealer.Seal(second, first);

        // Simulate the store dropping sub-microsecond ticks on write.
        first.OccurredAt = TruncateToMicrosecond(first.OccurredAt);
        second.OccurredAt = TruncateToMicrosecond(second.OccurredAt);

        validator.Validate([first, second]).IsIntact.Should().BeTrue();
    }

    [Fact]
    public void Seal_WithMicrosecondPrecisionTimestamp_ShouldHashTheExactUtcTicks()
    {
        using var provider = BuildProvider();
        var sealer = provider.GetRequiredService<IHashChainSealer<TimestampedEntry>>();
        var entry = new TimestampedEntry { OccurredAt = MicrosecondInstant.ToOffset(TimeSpan.FromHours(5)) };

        sealer.Seal(entry, null);

        entry.Hash.Should().Equal(ExpectedGenesisHash(MicrosecondInstant.UtcTicks));
    }

    [Fact]
    public void Seal_WithSubMicrosecondTicks_ShouldHashLikeTheTruncatedInstant()
    {
        using var provider = BuildProvider();
        var sealer = provider.GetRequiredService<IHashChainSealer<TimestampedEntry>>();
        var precise = new TimestampedEntry { OccurredAt = MicrosecondInstant.AddTicks(9) };
        var truncated = new TimestampedEntry { OccurredAt = MicrosecondInstant };

        sealer.Seal(precise, null);
        sealer.Seal(truncated, null);

        precise.Hash.Should().Equal(truncated.Hash);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHashChain<TimestampedEntry, TimestampedEntryCanonicalizer>();
        return services.BuildServiceProvider();
    }

    private static DateTimeOffset TruncateToMicrosecond(DateTimeOffset value)
    {
        return value.AddTicks(-(value.UtcTicks % TimeSpan.TicksPerMicrosecond));
    }

    // Rebuilds the genesis payload by hand: scheme version, sequence, empty previous hash, then the timestamp field.
    private static byte[] ExpectedGenesisHash(long utcTicks)
    {
        var payload = new List<byte>();
        AppendField(payload, 2, BigEndian(1));
        AppendField(payload, 3, BigEndian(1L));
        AppendField(payload, 5, []);
        AppendField(payload, 4, BigEndian(utcTicks));
        return SHA256.HashData(payload.ToArray());
    }

    private static void AppendField(List<byte> payload, byte tag, byte[] value)
    {
        payload.Add(tag);
        payload.AddRange(BigEndian(value.Length));
        payload.AddRange(value);
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] BigEndian(long value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    private sealed class TimestampedEntry : IHashChainedEntry
    {
        public long Sequence { get; set; }

        public byte[] PreviousHash { get; set; } = [];

        public byte[] Hash { get; set; } = [];

        public DateTimeOffset OccurredAt { get; set; }
    }

    private sealed class TimestampedEntryCanonicalizer : IChainedEntryCanonicalizer<TimestampedEntry>
    {
        public int SchemeVersion => 1;

        public void Write(TimestampedEntry entry, ICanonicalPayloadBuilder builder)
        {
            builder.Append(entry.OccurredAt);
        }
    }
}
