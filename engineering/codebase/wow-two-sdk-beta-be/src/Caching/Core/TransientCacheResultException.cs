namespace WoW.Two.Sdk.Beta.Be.Caching.Core;

/// <summary>Carries a transient factory value past the cache, which never stores a factory that throws.</summary>
/// <remarks>Internal control flow for <see cref="ICacheRepository.GetOrCreateUnlessTransientAsync{T}"/>; never escapes it.</remarks>
internal sealed class TransientCacheResultException : Exception
{
    /// <summary>Creates the carrier for <paramref name="value"/>.</summary>
    /// <param name="value">The transient value to hand back to the caller.</param>
    public TransientCacheResultException(object? value)
        : base("A cache factory produced a transient value.")
    {
        Value = value;
    }

    /// <summary>Gets the transient value.</summary>
    public object? Value { get; }
}
