# WoW.Two.Sdk.Beta.Be.Web.Compression

> Response compression — Brotli + Gzip with `Fastest` level.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Web.Compression
```

## Usage

```csharp
builder.Services.AddBrotliGzipCompression();

var app = builder.Build();
app.UseResponseCompression();
```
