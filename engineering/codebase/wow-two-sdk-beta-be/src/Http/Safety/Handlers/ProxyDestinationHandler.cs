using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Http.Safety.Handlers;

internal sealed class ProxyDestinationHandler(
    Uri source,
    Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri? target = request.RequestUri;
        if (target is null || !target.IsAbsoluteUri || target.UserInfo.Length != 0
            || target.Scheme != source.Scheme || target.Port != source.Port
            || !string.Equals(target.IdnHost.TrimEnd('.'), source.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
            || request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            throw new OutboundAddressBlockedException(target?.IsAbsoluteUri == true ? target.IdnHost : "(relative)");

        if (request.Version.Major >= 3 || request.VersionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
            throw new InvalidOperationException("Explicit proxy fetching requires HTTP/1 or HTTP/2 without upgrades.");

        if (request.Headers.Host is not null || request.Headers.Contains("Proxy-Authorization"))
            throw new InvalidOperationException("Explicit proxy fetching controls the destination host and proxy credentials.");

        await SafeOutboundProxy.ResolvePublicAsync(target.IdnHost, resolveAddresses, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
