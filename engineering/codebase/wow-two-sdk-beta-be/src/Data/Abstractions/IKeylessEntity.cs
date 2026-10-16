namespace WoW.Two.Sdk.Beta.Be.Data.Abstractions;

/// <summary>Defines an entity with no key at all — a view- or query-backed read shape.</summary>
/// <remarks>Declared so a missing key reads as a choice rather than an oversight.</remarks>
public interface IKeylessEntity : IEntity;
