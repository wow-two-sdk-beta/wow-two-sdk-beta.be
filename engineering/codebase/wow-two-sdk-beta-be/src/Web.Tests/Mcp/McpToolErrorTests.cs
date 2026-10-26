using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using WoW.Two.Sdk.Beta.Be.Ai.Mcp;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.Mcp;

/// <summary>A failed tool call reaches the agent as a typed, display-safe error that says whether a retry may succeed.</summary>
public sealed class McpToolErrorTests
{
    [Fact]
    public async Task TransientAppError_IsRetryableWithItsOwnMessage()
    {
        await using var app = await McpTestHost.StartAsync<FailingTools>();
        using HttpClient client = McpTestHost.Client(app);

        McpToolError error = Error(await McpTestHost.CallToolAsync(client, "timeout"));

        error.Should().Be(new McpToolError
        {
            Code = "OperationTimeout", Message = "The runner did not answer in time.", Retryable = true,
        });
    }

    [Fact]
    public async Task RefusedAppError_IsNotRetryable()
    {
        await using var app = await McpTestHost.StartAsync<FailingTools>();
        using HttpClient client = McpTestHost.Client(app);

        McpToolError error = Error(await McpTestHost.CallToolAsync(client, "conflict"));

        error.Code.Should().Be("Conflict");
        error.Message.Should().Be("The target is locked.");
        error.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task ValidationError_JoinsItsFailureMessages()
    {
        await using var app = await McpTestHost.StartAsync<FailingTools>();
        using HttpClient client = McpTestHost.Client(app);

        McpToolError error = Error(await McpTestHost.CallToolAsync(client, "invalid"));

        error.Code.Should().Be("Validation");
        error.Message.Should().Be("Slug is required. Days must be positive.");
        error.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task UnexpectedException_GetsTheGenericMessageOfItsKind()
    {
        await using var app = await McpTestHost.StartAsync<FailingTools>();
        using HttpClient client = McpTestHost.Client(app);

        JsonElement result = await McpTestHost.CallToolAsync(client, "crash");
        McpToolError error = Error(result);

        error.Code.Should().Be("Unexpected");
        error.Message.Should().Be(ErrorMessageConstants.For(AppErrorType.Unexpected));
        result.ToString().Should().NotContain("sensitive-sentinel");
    }

    [Fact]
    public void ToToolResult_DescribesAReturnedFailure()
    {
        var result = McpToolErrorFilter.ToToolResult(AppErrorFactory.ExternalUnavailable("The registry is down."));

        result.IsError.Should().BeTrue();
        var text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text;
        JsonSerializer.Deserialize<McpToolError>(text, Foundation.Serialization.JsonOptionsConstants.Default)
            .Should().Be(new McpToolError { Code = "ExternalUnavailable", Message = "The registry is down.", Retryable = true });
    }

    private static McpToolError Error(JsonElement response)
    {
        JsonElement result = response.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        string text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        return JsonSerializer.Deserialize<McpToolError>(text, Foundation.Serialization.JsonOptionsConstants.Default)!;
    }

    [McpServerToolType]
    private sealed class FailingTools
    {
        [McpServerTool(Name = "timeout", ReadOnly = true)]
        [Authorize]
        public static string Timeout() =>
            throw new AppException(AppErrorFactory.OperationTimeout("The runner did not answer in time."));

        [McpServerTool(Name = "conflict")]
        [Authorize]
        public static string Conflict() => throw new AppException(AppErrorFactory.Conflict("The target is locked."));

        [McpServerTool(Name = "invalid")]
        [Authorize]
        public static string Invalid() => throw new AppException(ValidationError.From(
        [
            new FieldError { Property = "slug", Message = "Slug is required.", Code = "required" },
            new FieldError { Property = "days", Message = "Days must be positive.", Code = "positive" },
        ]));

        [McpServerTool(Name = "crash", ReadOnly = true)]
        [Authorize]
        public static string Crash() => throw new InvalidOperationException("sensitive-sentinel /etc/secret");
    }
}
