namespace WoW.Two.Sdk.Backend.Beta.Caching.Core;

/// <summary>Creates the <see cref="CacheResult{T}"/> a factory returns to say whether its value may be cached.</summary>
public static class CacheResult
{
    /// <summary>Marks <paramref name="value"/> as cacheable.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to cache and return.</param>
    public static CacheResult<T> Keep<T>(T value) => new(value, IsTransient: false);

    /// <summary>Marks <paramref name="value"/> as transient: returned to the caller, never cached.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to return without caching, such as a fallback served while a source is down.</param>
    public static CacheResult<T> Transient<T>(T value) => new(value, IsTransient: true);
}

/// <summary>Represents a factory's value and whether it may be cached.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Value">The value returned to the caller.</param>
/// <param name="IsTransient">Whether the value is returned without being cached.</param>
public readonly record struct CacheResult<T>(T Value, bool IsTransient);
