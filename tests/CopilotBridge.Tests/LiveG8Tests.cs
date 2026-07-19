using System.Text.Json;
using ModelContextProtocol.Client;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class LiveG8Tests
{
    [Fact]
    public async Task OptInFreshConsultationsUseDistinctCopilotConversations()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("COPILOT_BRIDGE_LIVE_G8"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "CopilotBridge live G8 test",
            Command = Path.Combine(AppContext.BaseDirectory, "CopilotBridge.exe"),
            Arguments = ["--mcp"],
            WorkingDirectory = AppContext.BaseDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(2),
            StandardErrorLines = line => Console.WriteLine($"MCP server: {line}")
        });
        await using var client = await McpClient.CreateAsync(transport);
        var consult = Assert.Single(await client.ListToolsAsync(), tool => tool.Name == "consult_copilot");

        var first = await CallFreshAsync(consult, "COPILOT_BRIDGE_RC5_NEWCHAT_A");
        var second = await CallFreshAsync(consult, "COPILOT_BRIDGE_RC5_NEWCHAT_B");

        Assert.NotEqual(first.ConsultationId, second.ConsultationId);
        Assert.NotEqual(first.ConversationUrl, second.ConversationUrl);
    }

    private static async Task<LiveResult> CallFreshAsync(McpClientTool consult, string token)
    {
        var result = await consult.CallAsync(new Dictionary<string, object?>
        {
            ["requestMarkdown"] = $"这是 Copilot Bridge RC5 验收消息。请只回复精确文本 `{token}`，不要添加其他内容。",
            ["trigger"] = "user_explicit",
            ["newConversation"] = true
        });

        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("completed", content.GetProperty("status").GetString());
        var response = Assert.Single(content.GetProperty("responses").EnumerateArray());
        Assert.Contains(token, response.GetProperty("markdown").GetString(), StringComparison.Ordinal);

        return new LiveResult(
            content.GetProperty("consultationId").GetString()!,
            response.GetProperty("conversationUrl").GetString()!);
    }

    private sealed record LiveResult(string ConsultationId, string ConversationUrl);
}
