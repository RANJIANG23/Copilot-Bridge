using CopilotBridge.Core;
using CopilotBridge.Mcp;
using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class McpWorkspaceToolsTests
{
    [Fact]
    public async Task StdioVerticalSliceBuildsBoundedContextFromAuthorizedProjectsOnly()
    {
        var root = CreateRoot();
        try
        {
            var workspacePath = Path.Combine(root, "workspace");
            var workspace = new ConversationWorkspaceStore(workspacePath);
            await CreateSampleAsync(workspace, "关闭项目", ConversationAccessLevel.Off, "关闭会话", "phase18-needle off");
            await CreateSampleAsync(workspace, "元数据项目", ConversationAccessLevel.Metadata, "元数据会话", "phase18-needle metadata");
            await CreateSampleAsync(workspace, "片段项目", ConversationAccessLevel.Snippets, "片段会话", "phase18-needle snippets");
            var full = await CreateSampleAsync(
                workspace,
                "完整项目",
                ConversationAccessLevel.Full,
                "完整会话",
                "phase18-needle selected evidence",
                "Only this bounded answer belongs in the context package.",
                "This third turn must stay outside the selected page.");
            var settingsPath = Path.Combine(root, "settings.json");
            await new SettingsStore(settingsPath).SaveAsync(new BridgeSettings
            {
                ConversationWorkspaceDirectory = workspacePath,
                EdgeUserDataDirectory = Path.Combine(root, "missing-edge")
            });

            var before = HashWorkspace(workspacePath);
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "CopilotBridge Phase 18 isolated vertical slice",
                Command = ServerExecutablePath(),
                Arguments = ["--mcp", "--settings-path", settingsPath],
                WorkingDirectory = AppContext.BaseDirectory,
                ShutdownTimeout = TimeSpan.FromSeconds(1),
                StandardErrorLines = line => Console.WriteLine($"MCP server: {line}")
            });
            await using var client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync();
            var searchTool = Assert.Single(tools, tool => tool.Name == "search_conversations");
            var readTool = Assert.Single(tools, tool => tool.Name == "read_conversation");

            var visible = Structured(await searchTool.CallAsync(new Dictionary<string, object?>()));
            Assert.Equal(3, visible.GetProperty("results").GetArrayLength());
            Assert.DoesNotContain(
                visible.GetProperty("results").EnumerateArray(),
                item => item.GetProperty("displayTitle").GetString() == "关闭会话");

            var matches = Structured(await searchTool.CallAsync(new Dictionary<string, object?>
            {
                ["query"] = "phase18-needle",
                ["limit"] = 10
            }));
            var results = matches.GetProperty("results").EnumerateArray().ToArray();
            Assert.Equal(2, results.Length);
            Assert.DoesNotContain(results, item => item.GetProperty("accessLevel").GetString() == "metadata");
            Assert.Contains(results, item => item.GetProperty("accessLevel").GetString() == "snippets");
            var selected = Assert.Single(results, item => item.GetProperty("accessLevel").GetString() == "full");

            var page = Structured(await readTool.CallAsync(new Dictionary<string, object?>
            {
                ["conversationId"] = selected.GetProperty("conversationId").GetString(),
                ["startTurn"] = 0,
                ["maxTurns"] = 2
            })).GetProperty("conversation");
            Assert.True(page.GetProperty("hasMore").GetBoolean());
            var contextPackage = BuildContextPackage(page);
            Assert.Contains("phase18-needle selected evidence", contextPackage, StringComparison.Ordinal);
            Assert.Contains("Only this bounded answer", contextPackage, StringComparison.Ordinal);
            Assert.DoesNotContain("third turn", contextPackage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("snippets", contextPackage, StringComparison.OrdinalIgnoreCase);
            AssertWorkspaceUnchanged(before, HashWorkspace(workspacePath));

            var fullProject = Assert.Single(await workspace.GetProjectsAsync(), project => project.Id == full.ProjectId);
            await workspace.SetProjectAccessAsync(fullProject, ConversationAccessLevel.Off);
            var afterDowngrade = HashWorkspace(workspacePath);
            var blocked = Structured(await readTool.CallAsync(new Dictionary<string, object?>
            {
                ["conversationId"] = full.Id
            }));
            Assert.Equal("blocked", blocked.GetProperty("status").GetString());
            Assert.Equal("conversation_not_accessible", blocked.GetProperty("errorCode").GetString());
            AssertWorkspaceUnchanged(afterDowngrade, HashWorkspace(workspacePath));

            var persisted = await ConsultationPersistence.SaveWorkspaceRunAsync(
                "assist",
                new CollaborationRunResult(
                    [new ReviewerResult(
                        "primary",
                        contextPackage,
                        new AssistResult(
                            "Opus",
                            "Phase 18 retained answer",
                            "https://m365.cloud.microsoft/chat/phase-18",
                            1,
                            1,
                            false))],
                    1,
                    "https://m365.cloud.microsoft/chat/phase-18",
                    null,
                    null),
                await new SettingsStore(settingsPath).LoadAsync());
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.Turns.Count);
            Assert.Equal(contextPackage, persisted.Turns[0].Markdown);
            Assert.Equal("Phase 18 retained answer", persisted.Turns[1].Markdown);
            Assert.NotNull(await workspace.FindAsync(persisted.Id));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ReadOnlyToolsSearchAndPageOnlyAuthorizedLocalContent()
    {
        var root = CreateRoot();
        try
        {
            var workspacePath = Path.Combine(root, "workspace");
            var workspace = new ConversationWorkspaceStore(workspacePath);
            var project = await workspace.CreateProjectAsync("已授权项目");
            project = await workspace.SetProjectAccessAsync(project, ConversationAccessLevel.Full);
            var conversation = await workspace.CreateConversationAsync(project.Id, "架构复盘");
            await workspace.SaveAsync(conversation with
            {
                Turns =
                [
                    new ConversationTurn(DateTimeOffset.Now, "user", "检查本地知识复用方案"),
                    new ConversationTurn(DateTimeOffset.Now, "copilot", "结论 needle：保持显式授权。", "Opus", "verified")
                ]
            });
            var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
            await settingsStore.SaveAsync(new BridgeSettings
            {
                ConversationWorkspaceDirectory = workspacePath,
                EdgeUserDataDirectory = Path.Combine(root, "missing-edge")
            });
            await using var tools = new CopilotBridgeTools(settingsStore);
            var filesBefore = Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(workspacePath, path),
                    File.ReadAllText,
                    StringComparer.OrdinalIgnoreCase);

            var search = await tools.SearchConversationsAsync("needle");
            var result = Assert.Single(search.Results);
            var read = await tools.ReadConversationAsync(result.ConversationId, startTurn: 1, maxTurns: 1);
            var filesAfter = Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(workspacePath, path),
                    File.ReadAllText,
                    StringComparer.OrdinalIgnoreCase);

            Assert.Equal("completed", search.Status);
            Assert.Equal("full", result.AccessLevel);
            Assert.Equal("content", result.MatchScope);
            Assert.Contains("needle", result.Snippet, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("completed", read.Status);
            Assert.NotNull(read.Conversation);
            Assert.Equal("结论 needle：保持显式授权。", Assert.Single(read.Conversation!.Turns).Markdown);
            Assert.False(read.Conversation.HasMore);
            Assert.Equal(filesBefore.Keys.Order(), filesAfter.Keys.Order());
            foreach (var path in filesBefore.Keys) Assert.Equal(filesBefore[path], filesAfter[path]);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ReadOnlyToolsReturnStableErrorsWithoutConnectingToEdge()
    {
        var root = CreateRoot();
        try
        {
            var workspacePath = Path.Combine(root, "workspace");
            var workspace = new ConversationWorkspaceStore(workspacePath);
            var project = await workspace.CreateProjectAsync("片段项目");
            project = await workspace.SetProjectAccessAsync(project, ConversationAccessLevel.Snippets);
            var conversation = await workspace.CreateConversationAsync(project.Id, "片段会话");
            var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
            await settingsStore.SaveAsync(new BridgeSettings
            {
                ConversationWorkspaceDirectory = workspacePath,
                EdgeUserDataDirectory = Path.Combine(root, "missing-edge")
            });
            await using var tools = new CopilotBridgeTools(settingsStore);

            var inaccessibleProject = await tools.SearchConversationsAsync(projectId: "不存在");
            var inaccessibleConversation = await tools.ReadConversationAsync(conversation.Id);
            var invalidLimit = await tools.SearchConversationsAsync(limit: 21);

            Assert.Equal("project_not_accessible", inaccessibleProject.ErrorCode);
            Assert.Equal("conversation_not_accessible", inaccessibleConversation.ErrorCode);
            Assert.Equal("invalid_request", invalidLimit.ErrorCode);
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(),
        "CopilotBridge.Tests",
        Guid.NewGuid().ToString("N"));

    private static async Task<ConversationDocument> CreateSampleAsync(
        ConversationWorkspaceStore workspace,
        string projectName,
        ConversationAccessLevel accessLevel,
        string title,
        params string[] turns)
    {
        var project = await workspace.CreateProjectAsync(projectName);
        project = await workspace.SetProjectAccessAsync(project, accessLevel);
        var conversation = await workspace.CreateConversationAsync(project.Id, title);
        await workspace.SaveAsync(conversation with
        {
            Turns = turns.Select((markdown, index) => new ConversationTurn(
                DateTimeOffset.Now.AddMinutes(index),
                index % 2 == 0 ? "user" : "copilot",
                markdown)).ToArray()
        });
        return conversation;
    }

    private static JsonElement Structured(ModelContextProtocol.Protocol.CallToolResult result) =>
        Assert.IsType<JsonElement>(result.StructuredContent);

    private static string BuildContextPackage(JsonElement page)
    {
        var turns = page.GetProperty("turns").EnumerateArray()
            .Select(turn => $"[{turn.GetProperty("role").GetString()}] {turn.GetProperty("markdown").GetString()}");
        return $"# Authorized local context\nConversation: {page.GetProperty("displayTitle").GetString()}\n" +
               string.Join("\n", turns);
    }

    private static Dictionary<string, string> HashWorkspace(string workspacePath) =>
        Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(workspacePath, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.OrdinalIgnoreCase);

    private static void AssertWorkspaceUnchanged(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var path in expected.Keys) Assert.Equal(expected[path], actual[path]);
    }

    private static string ServerExecutablePath() =>
        Path.Combine(AppContext.BaseDirectory, "CopilotBridge.exe");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
