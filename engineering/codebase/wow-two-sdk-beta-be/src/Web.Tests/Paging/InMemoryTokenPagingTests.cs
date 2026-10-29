using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Paging;

/// <summary>Keyset pages over lists already in memory: every row once across ties, steady under new rows, ordinal
/// strings, and the same opaque token database reads use.</summary>
public sealed class InMemoryTokenPagingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    // Rows share submission times four at a time; ids mix case so a culture comparer would order them differently.
    private static readonly IReadOnlyList<Run> Runs =
    [
        .. Enumerable.Range(1, 23).Select(index => new Run((index % 2 == 0 ? "a" : "B") + index.ToString("D2"), Start.AddMinutes(index / 4))),
    ];

    [Fact]
    public void ToTokenPage_ShouldWalkEveryRowOnce_NewestFirst_AcrossTies()
    {
        var expected = Runs
            .OrderByDescending(run => run.At)
            .ThenByDescending(run => run.Id, StringComparer.Ordinal)
            .Select(run => run.Id)
            .ToList();

        var walked = Walk(token => Shuffled().ToTokenPage(run => run.At.UtcTicks, run => run.Id, new TokenPageApiRequest { PageToken = token, PageSize = 5 }, descending: true));

        walked.Should().Equal(expected);
    }

    [Fact]
    public void ToTokenPage_ShouldOrderStringsOrdinally_OnOneKey()
    {
        var walked = Walk(token => Runs.ToTokenPage(run => run.Id, new TokenPageApiRequest { PageToken = token, PageSize = 4 }));

        walked.Should().Equal(Runs.Select(run => run.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ToTokenPage_ShouldStaySteady_WhenANewerRowArrives()
    {
        var first = Runs.ToTokenPage(run => run.At, run => run.Id, new TokenPageApiRequest { PageSize = 6 }, descending: true);
        var grown = Runs.Append(new Run("Z99", Start.AddDays(1))).ToList();

        var second = grown.ToTokenPage(run => run.At, run => run.Id, new TokenPageApiRequest { PageSize = 6, PageToken = first.NextPageToken }, descending: true);
        var unchanged = Runs.ToTokenPage(run => run.At, run => run.Id, new TokenPageApiRequest { PageSize = 6, PageToken = first.NextPageToken }, descending: true);

        second.Items.Should().Equal(unchanged.Items);
        second.Items.Should().NotContain(run => run.Id == "Z99");
    }

    [Fact]
    public void ToTokenPage_ShouldEndWithoutAToken_AndClampTheSize()
    {
        var all = Runs.ToTokenPage(run => run.Id, new TokenPageApiRequest { PageSize = 500 });
        var empty = Array.Empty<Run>().ToTokenPage(run => run.Id, new TokenPageApiRequest());

        all.Items.Should().HaveCount(23);
        all.NextPageToken.Should().BeNull();
        all.PageSize.Should().Be(PagingConstants.MaxPageSize);
        empty.Items.Should().BeEmpty();
        empty.PageSize.Should().Be(PagingConstants.DefaultPageSize);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("WyJ4Il0")]
    public void ToTokenPage_ShouldRefuseATokenItDidNotWrite(string token)
    {
        var refusal = () => Runs.ToTokenPage(run => run.At.UtcTicks, run => run.Id, new TokenPageApiRequest { PageToken = token });

        refusal.Should().Throw<AppException>().Which.Error.Type.Should().Be(AppErrorType.Validation);
    }

    [Fact]
    public void PageTokenMapper_ShouldRoundTripKeys_AndTryDecodeWithoutThrowing()
    {
        var token = PageTokenMapper.Encode([42L, "B07"]);

        PageTokenMapper.TryDecode(token, [typeof(long), typeof(string)], out var keys).Should().BeTrue();
        keys.Should().Equal(42L, "B07");
        PageTokenMapper.TryDecode(token, [typeof(long)], out _).Should().BeFalse();
        PageTokenMapper.TryDecode("%%%", [typeof(long)], out _).Should().BeFalse();
        PageTokenMapper.TryDecode(new string('A', PageTokenMapper.MaxTokenLength + 1), [typeof(long)], out _).Should().BeFalse();
        PageTokenMapper.TryDecode(null, [typeof(long)], out _).Should().BeFalse();
    }

    private static IEnumerable<Run> Shuffled() => Runs.OrderBy(run => run.Id.GetHashCode(StringComparison.Ordinal));

    private static List<string> Walk(Func<string?, TokenPageDto<Run>> read)
    {
        var ids = new List<string>();
        string? token = null;
        for (var guard = 0; guard < 100; guard++)
        {
            var page = read(token);
            ids.AddRange(page.Items.Select(run => run.Id));
            token = page.NextPageToken;
            if (token is null)
                return ids;
        }

        throw new InvalidOperationException("Paging did not end.");
    }

    private sealed record Run(string Id, DateTimeOffset At);
}
