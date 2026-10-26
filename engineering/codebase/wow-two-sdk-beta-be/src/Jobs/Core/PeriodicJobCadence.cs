namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>Specifies how a periodic job spaces its passes.</summary>
public enum PeriodicJobCadence
{
    /// <summary>The interval runs from the end of one pass to the start of the next, so a slow pass never queues another.</summary>
    FixedDelay,

    /// <summary>Passes start on a fixed tick; a pass that overruns a tick skips it rather than running twice.</summary>
    FixedRate,
}
