using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Jobs.Core;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Jobs.Core;

/// <summary>
/// A wakeable schedule: the first pass reads, a wake reads at once, open work reads when the last read named, idle reads
/// once per safety interval, and a wake during a read is never lost.
/// </summary>
public sealed class WakeableScheduleTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-27T10:00:00Z"));

    private WakeableSchedule Schedule() => new(_time);

    [Fact]
    public void TryBegin_ShouldReadOnTheFirstPass_SoARestartLearnsWhatIsInFlight() =>
        Schedule().TryBegin(out _).Should().BeTrue();

    [Fact]
    public void TryBegin_ShouldReadOncePerIdleInterval_WhenNothingIsUnfinished()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket).Should().BeTrue();
        schedule.Settle(ticket, open: false, next: null, Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(TimeSpan.FromMinutes(4));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(50));
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadAtOnce_WhenWorkWasSubmitted()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: false, next: null, Idle);

        schedule.Wake();

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadWhenTheLastReadNamed_WhileWorkIsOpen()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: TimeSpan.FromSeconds(20), Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        // A pass is timed from the end of the one before; it may read a moment before the named time.
        _time.Advance(TimeSpan.FromSeconds(9.5));
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldReadOnTheNextPass_WhenTheReadNamedNoTimeOrAPastOne()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: null, Idle);
        schedule.TryBegin(out ticket).Should().BeTrue();
        schedule.Settle(ticket, open: true, next: TimeSpan.FromSeconds(-3), Idle);
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void TryBegin_ShouldWaitAtMostTheIdleInterval_WhenTheReadNamedALaterTime()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Settle(ticket, open: true, next: TimeSpan.FromHours(1), Idle);

        _time.Advance(Idle);

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void Settle_ShouldKeepTheNextPassDue_WhenWorkWasSubmittedDuringTheRead()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket).Should().BeTrue();

        schedule.Wake(); // the read may have listed the submissions before this one was written
        schedule.Settle(ticket, open: false, next: null, Idle);

        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void Missed_ShouldReadAgainOnTheNextPass_WhileWorkWasUnfinished_AndAfterTheIdleIntervalOtherwise()
    {
        var schedule = Schedule();
        schedule.TryBegin(out var ticket);
        schedule.Missed(ticket, Idle);
        schedule.TryBegin(out ticket).Should().BeTrue("nothing is known yet, so work counts as unfinished");

        schedule.Settle(ticket, open: false, next: null, Idle);
        _time.Advance(Idle);
        schedule.TryBegin(out ticket).Should().BeTrue();
        schedule.Missed(ticket, Idle);

        _time.Advance(TimeSpan.FromSeconds(10));
        schedule.TryBegin(out _).Should().BeFalse();
        _time.Advance(Idle);
        schedule.TryBegin(out _).Should().BeTrue();
    }

    [Fact]
    public void IsOpen_ShouldFollowTheLastSettledRead()
    {
        var schedule = Schedule();
        schedule.IsOpen.Should().BeTrue("nothing is known before the first read");
        schedule.TryBegin(out var ticket);

        schedule.Settle(ticket, open: false, next: null, Idle);

        schedule.IsOpen.Should().BeFalse();
    }
}

/// <summary>A job's interval read from configuration: counted in its unit, clamped, off at zero or less.</summary>
public sealed class PeriodicJobOptionsExtensionsTests
{
    [Theory]
    [InlineData(null, 10, 10)]
    [InlineData("30", 10, 30)]
    [InlineData("0", 10, 0)]
    [InlineData("-5", 10, 0)]
    [InlineData("1", 10, 2)]
    [InlineData("9000", 10, 300)]
    public void UseConfiguredInterval_ShouldCountClampAndTurnOff(string? configured, int fallback, int expectedSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:FollowSeconds"] = configured })
            .Build();

        var options = new PeriodicJobOptions().UseConfiguredInterval(configuration, "Jobs:FollowSeconds", fallback, TimeSpan.FromSeconds(1), 2, 300);

        options.Interval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void UseConfiguredInterval_ShouldCountInItsUnit()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = new PeriodicJobOptions().UseConfiguredInterval(configuration, "Operations:VitalsSampleMinutes", 5, TimeSpan.FromMinutes(1));

        options.Interval.Should().Be(TimeSpan.FromMinutes(5));
    }
}
