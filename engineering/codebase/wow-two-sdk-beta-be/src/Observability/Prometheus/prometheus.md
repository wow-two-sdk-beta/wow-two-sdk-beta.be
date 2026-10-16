# WoW.Two.Sdk.Beta.Be.Observability.Prometheus

> Prometheus scrape exporter for OTel metrics.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Observability.Prometheus
```

## Usage

```csharp
builder.Services.AddOpenTelemetryMetrics("my-service");
builder.Services.AddPrometheusMetricsExporter();

var app = builder.Build();
app.MapPrometheusScrapingEndpoint();   // exposes /metrics by default
```
