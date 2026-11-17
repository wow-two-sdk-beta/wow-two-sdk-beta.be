# Instagram provider client

`IInstagramClient.GetBusinessDiscoveryMediaAsync` reads one bounded media page
from Meta's Instagram API with Facebook Login. It returns
`Result<InstagramMediaPage, InstagramFailure>`; caller cancellation throws.

```csharp
services.AddInstagramIntegration(
    provider => provider.GetRequiredService<InstagramTokenSource>(),
    options => options.BaseAddress = new Uri("https://graph.facebook.com/"));

var result = await client.GetBusinessDiscoveryMediaAsync(new InstagramMediaRequest
{
    ProfessionalAccountId = queryingAccountId,
    ApiVersion = configuredApiVersion,
    Username = publisherUsername,
    PageSize = 25,
    After = continuationCursor
}, cancellationToken);
```

The explicit factory selects this provider's `IAccessTokenService`. The client
resolves its token on every call, never from a global or inbound credential.
Hosts own secret storage, rotation, activation and account authorization.

`InstagramMediaPage.Media` contains detached JSON objects using Meta's selected
media fields, including carousel children. Provider-shaped JSON preserves the
consumer's existing field mapping without introducing a Harvest dependency.
Only requested media fields are retained. Error bodies and paging URLs are not.

`After` comes from `paging.cursors.after`, independently of `paging.next`.
The final nonempty page may have a cursor: the next empty page terminates.
Malformed or non-advancing cursors on nonempty pages fail; empty pages ignore
paging metadata and terminate. Callers additionally bound pages and
detect cycles spanning more than one request.

Both result cases carry `InstagramRequestDiagnostics`: status, bytes, request
count, retry delay and numeric provider code. `RequestUrl` excludes the query,
so it contains neither bearer credentials nor cursors. Media content and the
successful continuation cursor are data, not diagnostic tags.

The factory registration makes at most one transport attempt per call, with
SDK circuit breaking/timeouts and no automatic retry. The caller decides when
to retry a rate-limited request. HTTP 429/401/403 retain their meanings even
when Meta sends a non-JSON error page. Transport failure details are sanitized.
