namespace WoW.Two.Sdk.Beta.Be.Http.Safety;

/// <summary>Defines the requests allowed through an explicitly configured proxy.</summary>
public enum OutboundProxyTraffic
{
    /// <summary>Allows GET and HEAD on one HTTP or HTTPS origin.</summary>
    WebRead,
    /// <summary>Allows GraphQL GET and POST requests on one exact HTTPS endpoint path.</summary>
    GraphQl,
    /// <summary>Allows native gRPC over TLS and HTTP/2 on one service path.</summary>
    Grpc,
}
