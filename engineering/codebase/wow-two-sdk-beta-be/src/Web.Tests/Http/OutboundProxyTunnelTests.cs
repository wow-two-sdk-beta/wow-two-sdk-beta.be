using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Http;

public sealed class OutboundProxyTunnelTests
{
    private const string Origin = "https://source.example";
    private static readonly Uri Proxy = new("http://proxy.example:3128");

    [Fact]
    public async Task ForwardProxy_ShouldReceiveTheAbsoluteHttpReadUrl()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var target = new Uri("http://source.example/read");
        Task proxy = RespondToForwardedReadAsync(listener, ct);
        try
        {
            using var client = new HttpClient(CreateLoopbackTransport(target, OutboundProxyTraffic.WebRead, listener));
            Assert.Equal("web", await client.GetStringAsync(target, ct));
        }
        finally
        {
            await deadline.CancelAsync();
            try { await proxy; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData(OutboundProxyTraffic.WebRead, "/read")]
    [InlineData(OutboundProxyTraffic.GraphQl, "/graphql")]
    [InlineData(OutboundProxyTraffic.Grpc, "/example.Catalog/")]
    public async Task ConnectTunnel_ShouldPreserveTheRequestsAndResponse(OutboundProxyTraffic traffic, string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        CancellationToken ct = deadline.Token;
        using RSA key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=source.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("source.example");
        certificateRequest.CertificateExtensions.Add(san.Build());
        using X509Certificate2 certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
        {
            listen.Protocols = HttpProtocols.Http1AndHttp2;
            listen.UseHttps(certificate);
        }));
        await using WebApplication app = builder.Build();
        var firstFrameRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapGet("/read", () => "web");
        app.MapPost("/graphql", async (HttpContext context) =>
        {
            using var body = new StreamReader(context.Request.Body);
            return await body.ReadToEndAsync(context.RequestAborted);
        });
        app.MapPost("/example.Catalog/List", async (HttpContext context) =>
        {
            Assert.Equal("HTTP/2", context.Request.Protocol);
            context.Response.ContentType = "application/grpc";
            await context.Response.Body.WriteAsync(new byte[5], context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await firstFrameRead.Task.WaitAsync(context.RequestAborted);
            await context.Response.Body.WriteAsync(new byte[5], context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        });
        await app.StartAsync(ct);

        using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        Task tunnel = RelayAsync(proxyListener, new Uri(app.Urls.Single()).Port, ct);
        try
        {
            using var client = new HttpClient(CreateLoopbackTransport(new Uri(Origin + path), traffic, proxyListener, certificate));
            if (traffic == OutboundProxyTraffic.WebRead)
            {
                Assert.Equal("web", await client.GetStringAsync(Origin + path, ct));
            }
            else if (traffic == OutboundProxyTraffic.GraphQl)
            {
                const string query = """{"query":"{ catalog { id } }"}""";
                using var payload = new StringContent(query, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(Origin + path, payload, ct);
                response.EnsureSuccessStatusCode();
                Assert.Equal(query, await response.Content.ReadAsStringAsync(ct));
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Origin + path + "List")
                {
                    Content = new ByteArrayContent(new byte[5]),
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
                request.Headers.TE.ParseAdd("trailers");
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                Assert.Equal(HttpVersion.Version20, response.Version);
                using Stream body = await response.Content.ReadAsStreamAsync(ct);
                var frame = new byte[5];
                await body.ReadExactlyAsync(frame, ct);
                Assert.Equal(new byte[5], frame);
                firstFrameRead.SetResult();
                await body.ReadExactlyAsync(frame, ct);
                Assert.Equal(new byte[5], frame);
                Assert.Equal(0, await body.ReadAsync(frame, ct));
                Assert.Equal("0", Assert.Single(response.TrailingHeaders.GetValues("grpc-status")));
            }
        }
        finally
        {
            await deadline.CancelAsync();
            try { await tunnel; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

    private static DelegatingHandler CreateLoopbackTransport(
        Uri source, OutboundProxyTraffic traffic, TcpListener listener, X509Certificate2? certificate = null)
    {
        var handler = (DelegatingHandler)SafeOutboundProxy.CreateHandler(source, Proxy, traffic,
            resolveAddresses: (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1") }));
        var sockets = (SocketsHttpHandler)handler.InnerHandler!;
        // Route the proxy socket to the test listener. The production handler still constructs CONNECT and negotiates TLS.
        sockets.ConnectCallback = async (connection, ct) =>
        {
            Assert.Equal(Proxy.Host, connection.DnsEndPoint.Host);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(listener.LocalEndpoint, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        if (certificate is not null)
        {
            sockets.SslOptions.RemoteCertificateValidationCallback = (_, peer, _, _) =>
                peer?.GetCertHashString() == certificate.GetCertHashString();
        }
        return handler;
    }

    private static async Task RespondToForwardedReadAsync(TcpListener listener, CancellationToken ct)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(ct);
        using NetworkStream connection = client.GetStream();
        using var reader = new StreamReader(connection, Encoding.ASCII, leaveOpen: true);
        Assert.Equal("GET http://source.example/read HTTP/1.1", await reader.ReadLineAsync(ct));
        await ReadHeadersAsync(reader, ct);
        await connection.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nweb"u8.ToArray(), ct);
    }

    private static async Task RelayAsync(TcpListener listener, int port, CancellationToken ct)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(ct);
        using NetworkStream connection = client.GetStream();
        using var reader = new StreamReader(connection, Encoding.ASCII, leaveOpen: true);
        Assert.Equal("CONNECT source.example:443 HTTP/1.1", await reader.ReadLineAsync(ct));
        await ReadHeadersAsync(reader, ct);
        using var server = new TcpClient();
        await server.ConnectAsync(IPAddress.Loopback, port, ct);
        using NetworkStream destination = server.GetStream();
        await connection.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct);
        using var relay = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task outbound = connection.CopyToAsync(destination, relay.Token);
        Task inbound = destination.CopyToAsync(connection, relay.Token);
        await Task.WhenAny(outbound, inbound);
        await relay.CancelAsync();
        try { await Task.WhenAll(outbound, inbound); }
        catch (OperationCanceledException) when (relay.IsCancellationRequested) { }
    }

    private static async Task ReadHeadersAsync(StreamReader reader, CancellationToken ct)
    {
        string? header;
        do { header = await reader.ReadLineAsync(ct); } while (!string.IsNullOrEmpty(header));
    }
}
