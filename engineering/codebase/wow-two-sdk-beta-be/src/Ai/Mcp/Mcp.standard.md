# MCP host contract

- `AddStatelessMcpServer` MUST use the official MCP C# SDK Streamable HTTP transport.
- Registration MUST enable authorization filters for tool, resource and prompt policies.
- The host MUST register its authentication, authorization policies and explicit tool types.
- `MapAuthenticatedMcp` MUST require the caller's named authorization policy.
- `MapAuthenticatedMcp` MUST refuse a request whose `Origin` is neither the endpoint's own nor configured, with 403.
- `MapAuthenticatedMcp` MUST accept a request without `Origin`.
- `MapAuthenticatedMcp` MUST refuse a body over the configured limit with 413, whether declared or chunked.
- Transport MUST remain stateless and MUST disable legacy SSE endpoints.
- Registration MUST NOT scan assemblies, register product tools or expose an endpoint automatically.
- A failed tool call MUST return `isError` with a `McpToolError`; only an `AppError`'s own message MAY reach the agent.
- Tool implementations MUST preserve caller cancellation and MUST NOT expose unexpected exception details.
- Authentication and private-network deployment remain the host's responsibility.

Protocol: [official C# SDK](https://csharp.sdk.modelcontextprotocol.io/v2/).
