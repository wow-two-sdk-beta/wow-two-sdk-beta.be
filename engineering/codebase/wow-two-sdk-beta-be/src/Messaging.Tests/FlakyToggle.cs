using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Messaging.Reliability;
using WoW.Two.Sdk.Beta.Be.Testing.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Messaging.Tests;

/// <summary>The "deploy the fix" switch a redrive test flips between laps.</summary>
public sealed class FlakyToggle
{
    private volatile bool _fail = true;

    /// <summary>Whether <see cref="FlakyHandler"/> throws. Written from the test thread, read on a consume worker.</summary>
    public bool Fail
    {
        get => _fail;
        set => _fail = value;
    }
}
