using CopilotBridge.Core;
using CopilotBridge.Mcp;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class McpWorkspaceToolsTests
{
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

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
