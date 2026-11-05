using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Jobs.Core;

/// <summary>
/// Person-bound operations: which reaction a signal meets, who stays untouched, the caller's token and disposal, and a signal
/// remembered for work that starts late.
/// </summary>
public sealed class PersonOperationTrackerTests : IDisposable
{
    private const string Person = "person-1";
    private const string Organization = "org-1";
    private const string Session = "session-1";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-05T10:00:00Z"));
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();
    }

    [Fact]
    public void Default_ShouldContinueOnSignOut_AndCancelOnAccessLoss()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation());

        tracker.SessionEnded(Session, Person).Should().Be(0);
        lease.CancellationToken.IsCancellationRequested.Should().BeFalse();
        lease.CancelReason.Should().BeNull();

        tracker.AccessLost(Person, Organization).Should().Be(1);
        lease.CancellationToken.IsCancellationRequested.Should().BeTrue();
        lease.CancelReason.Should().Be("access_lost");
    }

    [Fact]
    public void LiveOperation_TiedToASession_ShouldCancelOnThatSignOutOnly()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation() with { SessionId = Session });

        tracker.SessionEnded("session-2", Person).Should().Be(0, "another sign-in of the same person ended");
        lease.CancelReason.Should().BeNull();

        tracker.SessionEnded(Session, Person).Should().Be(1);
        lease.CancellationToken.IsCancellationRequested.Should().BeTrue();
        lease.CancelReason.Should().Be("session_ended");
    }

    [Fact]
    public void Operation_TiedToASession_ShouldContinue_WhenItDeclaredContinue()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation() with { SessionId = Session, OnSessionEnded = PersonOperationReaction.Continue });

        tracker.SessionEnded(Session, Person).Should().Be(0);

        lease.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void OnSessionEndedCancel_WithNoSession_ShouldMatchBySubject()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation() with { OnSessionEnded = PersonOperationReaction.Cancel });

        tracker.SessionEnded(Session, "person-2").Should().Be(0);
        lease.CancelReason.Should().BeNull();

        tracker.SessionEnded(Session, Person).Should().Be(1);
        lease.CancelReason.Should().Be("session_ended");
    }

    [Fact]
    public void AnotherPerson_ShouldStayUntouched()
    {
        var tracker = Tracker();
        using var mine = tracker.Begin(Operation() with { OnSessionEnded = PersonOperationReaction.Cancel, OrganizationId = Organization });
        using var theirs = tracker.Begin(Operation() with { Subject = "person-2", OnSessionEnded = PersonOperationReaction.Cancel, OrganizationId = Organization, SessionId = "session-2" });

        tracker.SessionEnded(Session, Person).Should().Be(1);
        tracker.AccessLost(Person, Organization).Should().Be(0, "the person's own operation is already cancelled");

        mine.CancellationToken.IsCancellationRequested.Should().BeTrue();
        theirs.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void AccessLost_ShouldMatchTheOrganization_OrTheSubjectAloneWithNone()
    {
        var tracker = Tracker();
        using var inOrganization = tracker.Begin(Operation() with { OrganizationId = Organization });
        using var elsewhere = tracker.Begin(Operation() with { OrganizationId = "org-2" });
        using var noOrganization = tracker.Begin(Operation());
        using var blankOrganization = tracker.Begin(Operation() with { OrganizationId = "" });

        tracker.AccessLost(Person, Organization).Should().Be(3);

        inOrganization.CancellationToken.IsCancellationRequested.Should().BeTrue();
        elsewhere.CancellationToken.IsCancellationRequested.Should().BeFalse();
        noOrganization.CancellationToken.IsCancellationRequested.Should().BeTrue();
        blankOrganization.CancellationToken.IsCancellationRequested.Should().BeTrue("a missing claim must not escape the signal");
    }

    [Fact]
    public void OnAccessLostContinue_ShouldKeepRunning()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation() with { OnAccessLost = PersonOperationReaction.Continue });

        tracker.AccessLost(Person, Organization).Should().Be(0);

        lease.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CallersToken_ShouldStillCancel_WithNoReason()
    {
        var tracker = Tracker();
        using var caller = new CancellationTokenSource();
        using var lease = tracker.Begin(Operation(), caller.Token);

        caller.Cancel();

        lease.CancellationToken.IsCancellationRequested.Should().BeTrue();
        lease.CancelReason.Should().BeNull();
        tracker.AccessLost(Person, Organization).Should().Be(0, "the caller cancelled it first");
        lease.CancelReason.Should().BeNull();
    }

    [Fact]
    public void SecondSignal_ShouldNotCountAgain_NorReplaceTheReason()
    {
        var tracker = Tracker();
        using var lease = tracker.Begin(Operation() with { SessionId = Session });

        tracker.SessionEnded(Session, Person).Should().Be(1);
        tracker.AccessLost(Person, Organization).Should().Be(0);

        lease.CancelReason.Should().Be("session_ended");
    }

    [Fact]
    public void Disposal_ShouldRemoveTheLease_AndKeepItsTokenReadable()
    {
        var tracker = Tracker();
        var lease = tracker.Begin(Operation());

        lease.Dispose();
        lease.Dispose();

        tracker.AccessLost(Person, Organization).Should().Be(0);
        lease.CancelReason.Should().BeNull();
        lease.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void LateBegin_ShouldBeCancelledByASignalSinceItWasAccepted_AndNotOnceTheMemoryPassed()
    {
        var tracker = Tracker();
        var accepted = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromSeconds(1));
        tracker.AccessLost(Person, Organization).Should().Be(0);
        tracker.SessionEnded(Session, Person).Should().Be(0);

        _time.Advance(TimeSpan.FromMinutes(59));
        using (var late = tracker.Begin(Operation() with { OrganizationId = Organization, AcceptedAt = accepted }))
        {
            late.CancellationToken.IsCancellationRequested.Should().BeTrue();
            late.CancelReason.Should().Be("access_lost");
        }

        using (var lateLive = tracker.Begin(Operation() with { OnAccessLost = PersonOperationReaction.Continue, SessionId = Session, AcceptedAt = accepted }))
            lateLive.CancelReason.Should().Be("session_ended");

        using (var otherOrganization = tracker.Begin(Operation() with { OrganizationId = "org-2", AcceptedAt = accepted }))
            otherOrganization.CancellationToken.IsCancellationRequested.Should().BeFalse();

        using (var continuesOnSignOut = tracker.Begin(Operation() with { OnAccessLost = PersonOperationReaction.Continue, AcceptedAt = accepted }))
            continuesOnSignOut.CancellationToken.IsCancellationRequested.Should().BeFalse();

        _time.Advance(TimeSpan.FromMinutes(1));
        using var afterMemory = tracker.Begin(Operation() with { OrganizationId = Organization, SessionId = Session, AcceptedAt = accepted });
        afterMemory.CancellationToken.IsCancellationRequested.Should().BeFalse();
        afterMemory.CancelReason.Should().BeNull();
    }

    [Fact]
    public void NewWork_ShouldNotBeHeldToAnEarlierSignal_AfterTheAccessCameBack()
    {
        var tracker = Tracker();
        tracker.AccessLost(Person, Organization);
        _time.Advance(TimeSpan.FromMinutes(1));

        using (var asked = tracker.Begin(Operation() with { OrganizationId = Organization }))
            asked.CancellationToken.IsCancellationRequested.Should().BeFalse("no AcceptedAt: it starts as it is asked for");

        using var acceptedSince = tracker.Begin(Operation() with { OrganizationId = Organization, AcceptedAt = _time.GetUtcNow() });
        acceptedSince.CancellationToken.IsCancellationRequested.Should().BeFalse("accepted after the signal");
    }

    [Fact]
    public void SignalMemory_ShouldFollowTheOption_AndZeroTurnsItOff()
    {
        var accepted = _time.GetUtcNow() - TimeSpan.FromSeconds(1);
        var shorter = Tracker(o => o.SignalMemory = TimeSpan.FromMinutes(5));
        shorter.AccessLost(Person, Organization);
        _time.Advance(TimeSpan.FromMinutes(5));
        using (var late = shorter.Begin(Operation() with { AcceptedAt = accepted }))
            late.CancellationToken.IsCancellationRequested.Should().BeFalse();

        var off = Tracker(o => o.SignalMemory = TimeSpan.Zero);
        off.AccessLost(Person, Organization);
        using (var immediately = off.Begin(Operation() with { AcceptedAt = accepted }))
            immediately.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void Memory_ShouldBeSweptOnEachSignal_AndARepeatKeepsOneEntry()
    {
        var tracker = Tracker();
        for (var i = 0; i < 10; i++)
            tracker.SessionEnded($"session-{i}", Person);
        tracker.AccessLost(Person, Organization);
        tracker.AccessLost(Person, Organization);
        RememberedCount(tracker).Should().Be(11);

        _time.Advance(TimeSpan.FromHours(1));
        tracker.AccessLost("person-2", Organization);

        RememberedCount(tracker).Should().Be(1, "every older signal has expired");
    }

    [Fact]
    public void CallbackThatThrows_ShouldNotKeepTheSignalFromTheOthers()
    {
        var tracker = Tracker();
        using var failing = tracker.Begin(Operation() with { Name = "failing" });
        using var healthy = tracker.Begin(Operation() with { Name = "healthy" });
        failing.CancellationToken.Register(() => throw new InvalidOperationException("callback failed"));

        tracker.AccessLost(Person, Organization).Should().Be(2);

        failing.CancellationToken.IsCancellationRequested.Should().BeTrue();
        healthy.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Callback_ShouldReadTheReason_AndMayDisposeItsOwnLease()
    {
        var tracker = Tracker();
        var lease = tracker.Begin(Operation());
        string? reasonSeen = null;
        lease.CancellationToken.Register(() =>
        {
            reasonSeen = lease.CancelReason;
            lease.Dispose();
        });

        tracker.AccessLost(Person, Organization).Should().Be(1);

        reasonSeen.Should().Be("access_lost");
        lease.CancelReason.Should().Be("access_lost");
    }

    [Fact]
    public async Task Signals_ShouldRaceBeginAndDisposeSafely()
    {
        var tracker = Tracker(o => o.SignalMemory = TimeSpan.Zero);
        using var stop = new CancellationTokenSource();
        var signalling = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                tracker.AccessLost(Person, Organization);
                tracker.SessionEnded(Session, Person);
            }
        });
        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 5_000; i++)
            {
                using var lease = tracker.Begin(Operation() with { SessionId = Session });
                lease.CancellationToken.Register(static () => { });
            }
        }));

        await Task.WhenAll(workers);
        await stop.CancelAsync();
        await signalling;

        tracker.AccessLost(Person, Organization).Should().Be(0, "every lease was disposed");
    }

    [Fact]
    public void Begin_ShouldRejectABlankSubject()
    {
        var begin = () => Tracker().Begin(Operation() with { Subject = " " });

        begin.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Registration_ShouldKeepOneSingleton_AndValidateTheMemory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersonOperations();
        services.AddPersonOperations();
        services.Count(descriptor => descriptor.ServiceType == typeof(IPersonOperationTracker)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPersonOperationTracker>().Should().BeSameAs(provider.GetRequiredService<IPersonOperationTracker>());

        var negative = new ServiceCollection();
        negative.AddLogging();
        negative.AddPersonOperations(o => o.SignalMemory = TimeSpan.FromSeconds(-1));
        using var invalid = negative.BuildServiceProvider();
        var resolve = () => invalid.GetRequiredService<IPersonOperationTracker>();
        resolve.Should().Throw<OptionsValidationException>();
    }

    private static PersonOperationDeclaration Operation() => new() { Name = "export", Subject = Person };

    private static int RememberedCount(IPersonOperationTracker tracker)
    {
        var field = tracker.GetType().GetField("_remembered", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((ICollection)field.GetValue(tracker)!).Count;
    }

    private IPersonOperationTracker Tracker(Action<PersonOperationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddPersonOperations(configure);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IPersonOperationTracker>();
    }
}
