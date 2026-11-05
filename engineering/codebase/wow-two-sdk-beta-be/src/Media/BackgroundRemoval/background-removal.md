# Background removal

Optional, bounded HTTP adapter for host-owned segmentation workers. Configure URL/key explicitly; absent configuration reports unavailable. No model downloads occur.

```csharp
services.AddBackgroundRemoval(o => {
    o.BaseUrl = "http://127.0.0.1:8290";
    o.ApiKey = configuration["PrivateWorkerKey"];
});
```

A worker that is a sibling container on a private network the host trusts sets `o.AllowPrivateNetworkHttp = true` and a
single-label origin (`http://ocharo-dev-image-worker:8080`). The key then crosses that network unencrypted; a dotted name
or an address stays refused.

See [contract](BackgroundRemoval.standard.md) and [API](BackgroundRemoval.spec.md).
