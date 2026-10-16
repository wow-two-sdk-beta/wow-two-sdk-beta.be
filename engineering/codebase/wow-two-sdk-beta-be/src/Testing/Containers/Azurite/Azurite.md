# WoW.Two.Sdk.Beta.Be.Testing.Containers.Azurite

> Azurite (Azure Storage emulator) Testcontainers fixture.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Testing.Containers.Azurite
```

## Usage

```csharp
private readonly AzuriteFixture _azurite = new();

// Use _azurite.ConnectionString or _azurite.BlobEndpoint with Azure.Storage.Blobs etc.
```
