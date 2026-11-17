using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

/// <summary>Connects to Meta's Instagram API with Facebook Login for professional-account media discovery.</summary>
public interface IInstagramClient
{
    /// <summary>Reads one bounded media page using the explicitly selected token source.</summary>
    /// <param name="request">The querying account, publisher and continuation.</param>
    /// <param name="cancellationToken">Cancels the call without converting cancellation into a provider failure.</param>
    /// <returns>The media page or a sanitized failure with request diagnostics.</returns>
    Task<Result<InstagramMediaPage, InstagramFailure>> GetBusinessDiscoveryMediaAsync(
        InstagramMediaRequest request, CancellationToken cancellationToken = default);
}
