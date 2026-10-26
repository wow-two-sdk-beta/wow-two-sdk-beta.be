# MCP HTTP host

```csharp
services.AddStatelessMcpServer(guard =>
{
    guard.AllowedOrigins = ["http://localhost:6274"];   // MCP Inspector, besides the endpoint's own origin
    guard.MaxRequestBodyBytes = 64 * 1024;              // the default; null lifts the limit
}).WithTools<ProductTools>();
app.MapAuthenticatedMcp("/mcp", "IntegrationKey");

[McpServerTool(Name = "deploy"), Authorize(Policy = "deployments:write")]
public async Task<string> DeployAsync(string target, CancellationToken ct) =>
    await sender.Send(new DeployCommand(target), ct) switch
    {
        AppResult<string>.Success ok => ok.Data,
        AppResult<string>.Failure failed => throw new AppException(failed.Error),   // reaches the agent as McpToolError
        _ => throw new UnreachableException(),
    };
```

- `AddStatelessMcpServer` returns the official `IMcpServerBuilder` for explicit tool registration.
- `MapAuthenticatedMcp` requires a nonempty path and named policy; authentication middleware precedes it.
- Tool methods use the official `[McpServerTool]` and ASP.NET Core `[Authorize(Policy = "...")]` attributes.
- Authorization filters omit forbidden tools from discovery and deny direct invocation.
- Stateless requests use fresh authentication context and request-scoped dependencies.
- Existing `initialize` clients and current `server/discover` clients share the endpoint.

## Request guard

- `MapAuthenticatedMcp` wraps every endpoint it maps with `McpRequestGuardMiddleware`, after authorization and before
  the transport.
- A request carrying an `Origin` other than the endpoint's own (as forwarded headers resolve it) or one in
  `McpRequestGuardOptions.AllowedOrigins` gets 403; one without `Origin` passes. `McpOriginExtensions` holds the rule.
- The body is buffered up to `MaxRequestBodyBytes` (default 64 KiB), chunked bodies included; past it, 413.
- `AllowedOrigins` entries must be `scheme://host[:port]` over http or https; a malformed one fails the boot.
- `services.Configure<McpRequestGuardOptions>(configuration.GetSection("Mcp"))` binds the options from configuration.

## Tool errors

- `McpToolErrorFilter` is registered on every call-tool request: a tool that throws `AppException` returns
  `isError` with one text block, `{"code":"OperationTimeout","message":"…","retryable":true}`.
- `message` is the `AppError`'s own text (a `ValidationError` joins its failure messages); `retryable` is true for
  transient kinds (`IErrorNatureMapper`): timeouts, throttling, an unavailable dependency.
- Any other exception is logged and answered with its kind's generic message (`IExceptionMapper`).
- Caller cancellation and `McpProtocolException`s, such as a refused scope, pass through to the transport.
- `McpToolErrorFilter.ToToolResult(error)` builds the same result for a tool that returns its failure.
- MCP tools, resources, prompts and client capabilities follow the [official SDK](https://csharp.sdk.modelcontextprotocol.io/v2/).
