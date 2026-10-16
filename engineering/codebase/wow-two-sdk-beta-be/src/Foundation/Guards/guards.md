# WoW.Two.Sdk.Beta.Be.Guards

> Re-exports [Ardalis.GuardClauses](https://github.com/ardalis/GuardClauses) + adds wow-two-specific guards (slug, ULID).

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Guards
```

## Usage

```csharp
using Ardalis.GuardClauses;
using WoW.Two.Sdk.Beta.Be.Guards;

public sealed class ProductHandle
{
    public string Slug { get; }

    public ProductHandle(string slug)
    {
        Slug = Guard.Against.NotSlug(slug, nameof(slug));
    }
}
```

## See also

- [Ardalis.GuardClauses](https://github.com/ardalis/GuardClauses)
