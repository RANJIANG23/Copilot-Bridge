using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public async Task StdioServerExposesExactlyFourHonestlyAnnotatedTools()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "CopilotBridge protocol test",
            Command = ServerExecutablePath(),
            Arguments = ["--mcp"],
            WorkingDirectory = AppContext.BaseDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(1),
            StandardErrorLines = line => Console.WriteLine($"MCP server: {line}")
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            ["consult_copilot", "copilot_bridge_status", "read_conversation", "search_conversations"],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());
        var status = Assert.Single(tools, tool => tool.Name == "copilot_bridge_status");
        var consult = Assert.Single(tools, tool => tool.Name == "consult_copilot");
        var search = Assert.Single(tools, tool => tool.Name == "search_conversations");
        var read = Assert.Single(tools, tool => tool.Name == "read_conversation");
        Assert.True(status.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(status.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(status.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.True(search.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(search.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(search.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.True(read.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(read.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(read.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.False(consult.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(consult.ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(consult.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.DoesNotContain("mode", consult.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", consult.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("projectId", search.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("conversationId", read.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("collaboration mode", client.ServerInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local project access", client.ServerInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never retry", client.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("retryAction", client.ServerInstructions, StringComparison.Ordinal);

        var result = await status.CallAsync(new Dictionary<string, object?>());
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var statusContent = Assert.IsType<System.Text.Json.JsonElement>(result.StructuredContent);
        Assert.InRange(statusContent.GetProperty("assistTurnBudget").GetInt32(), 1, 20);
        Assert.InRange(statusContent.GetProperty("outsourceTurnBudget").GetInt32(), 1, 20);
        Assert.InRange(statusContent.GetProperty("reviewTurnBudget").GetInt32(), 1, 20);

        var safeFailure = await consult.CallAsync(new Dictionary<string, object?>
        {
            ["requestMarkdown"] = "",
            ["trigger"] = "user_explicit"
        });
        Assert.NotEqual(true, safeFailure.IsError);
        var safeFailureContent = Assert.IsType<System.Text.Json.JsonElement>(safeFailure.StructuredContent);
        Assert.Equal("not_submitted", safeFailureContent.GetProperty("status").GetString());
        Assert.True(safeFailureContent.GetProperty("canRetrySafely").GetBoolean());
        Assert.Equal("new_consultation", safeFailureContent.GetProperty("retryAction").GetString());
    }

    [Fact]
    public async Task StdioServerRemainsAvailableForMultipleRequestsFromOneClient()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "CopilotBridge lifecycle test",
            Command = ServerExecutablePath(),
            Arguments = ["--mcp"],
            WorkingDirectory = AppContext.BaseDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        });
        await using var client = await McpClient.CreateAsync(transport);
        var status = Assert.Single(
            await client.ListToolsAsync(),
            tool => tool.Name == "copilot_bridge_status");

        var first = await status.CallAsync(new Dictionary<string, object?>());
        var second = await status.CallAsync(new Dictionary<string, object?>());

        Assert.NotEqual(true, first.IsError);
        Assert.NotEqual(true, second.IsError);
        Assert.NotNull(first.StructuredContent);
        Assert.NotNull(second.StructuredContent);
    }

    [Fact]
    public async Task StdioServerExitsWhenClientClosesInput()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ServerExecutablePath(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--mcp" }
        }) ?? throw new InvalidOperationException("Could not start CopilotBridge MCP server.");

        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task McpSettingsPathMustBeAbsolute()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ServerExecutablePath(),
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--mcp", "--settings-path", "relative.json" }
        }) ?? throw new InvalidOperationException("Could not start CopilotBridge MCP server.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("absolute path", await process.StandardError.ReadToEndAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ServerExecutablePath() =>
        Path.Combine(AppContext.BaseDirectory, "CopilotBridge.exe");
}
