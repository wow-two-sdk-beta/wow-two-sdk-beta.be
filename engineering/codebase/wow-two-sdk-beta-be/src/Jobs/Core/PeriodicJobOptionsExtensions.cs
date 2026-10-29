using Microsoft.Extensions.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Jobs.Core;

/// <summary>Sets a periodic job's options from host configuration.</summary>
public static class PeriodicJobOptionsExtensions
{
    /// <summary>
    /// Sets <see cref="PeriodicJobOptions.Interval"/> from a whole number of <paramref name="unit"/>s under
    /// <paramref name="key"/>, within <paramref name="min"/> and <paramref name="max"/>; zero or less turns the job off.
    /// Read it in the options callback, so a test host's or local run's override still counts.
    /// </summary>
    /// <param name="options">The job's options.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="key">The configuration key, such as <c>Deployment:FollowSeconds</c>.</param>
    /// <param name="fallback">The count when the key is absent.</param>
    /// <param name="unit">The length of one count: <c>TimeSpan.FromSeconds(1)</c>, <c>TimeSpan.FromMinutes(1)</c>.</param>
    /// <param name="min">The smallest count kept above zero. Default 1.</param>
    /// <param name="max">The largest count. Default unbounded.</param>
    /// <returns>The same options.</returns>
    /// <example>
    /// <code>
    /// services.AddPeriodicJob&lt;Applies&gt;("apply", (applies, ct) =&gt; applies.RunDueAsync(ct), (options, root) =&gt;
    ///     options.UseConfiguredInterval(root.GetRequiredService&lt;IConfiguration&gt;(), "Jobs:ApplySeconds", 60, TimeSpan.FromSeconds(1), 10, 600));
    /// </code>
    /// </example>
    public static PeriodicJobOptions UseConfiguredInterval(
        this PeriodicJobOptions options, IConfiguration configuration, string key, int fallback, TimeSpan unit, int min = 1, int max = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(unit, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(min, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);

        var count = configuration.GetValue(key, fallback);
        options.Interval = count <= 0 ? TimeSpan.Zero : unit * Math.Clamp(count, min, max);
        return options;
    }
}
