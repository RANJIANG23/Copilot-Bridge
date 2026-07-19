using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public async Task StdioServerExposesExactlyTwoHonestlyAnnotatedTools()
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
            ["consult_copilot", "copilot_bridge_status"],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());
        var status = Assert.Single(tools, tool => tool.Name == "copilot_bridge_status");
        var consult = Assert.Single(tools, tool => tool.Name == "consult_copilot");
        Assert.True(status.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(status.ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(status.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.False(consult.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(consult.ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(consult.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.DoesNotContain("mode", consult.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", consult.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collaboration mode", client.ServerInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never retry", client.ServerInstructions, StringComparison.Ordinal);

        var result = await status.CallAsync(new Dictionary<string, object?>());
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
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

    private static string ServerExecutablePath() =>
        Path.Combine(AppContext.BaseDirectory, "CopilotBridge.exe");
}
