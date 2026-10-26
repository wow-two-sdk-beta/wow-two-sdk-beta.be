# MCP

Opt-in, authenticated, stateless Streamable HTTP hosting for product-owned MCP tools, screened by default.

```csharp
services.AddStatelessMcpServer(guard => guard.AllowedOrigins = ["http://localhost:6274"]).WithTools<ProductTools>();
app.MapAuthenticatedMcp("/mcp", "IntegrationKey");
```

- [Contract](Mcp.standard.md) · [API and wiring](Mcp.spec.md)
- Tool policies use the host's authorization registrations.
- Every mapped endpoint refuses a foreign browser `Origin` (403) and a body over 64 KiB (413); a CLI client sends no
  `Origin` and passes.
- A failed tool call returns `isError` with a `McpToolError` (`code`, `message`, `retryable`); exception text never
  reaches the agent.
- No tool, scope, identity provider or network exposure is enabled implicitly.
