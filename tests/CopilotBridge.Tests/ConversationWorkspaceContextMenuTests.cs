using CopilotBridge.Core;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class ConversationWorkspaceContextMenuTests
{
    [Fact]
    public async Task RenamingCustomProjectMovesMarkdownAndUpdatesItsProjectId()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("待改名项目");
            var conversation = await store.CreateConversationAsync(project.Id, "会话标题");

            var renamed = await store.RenameProjectAsync(project, "已改名项目");
            var reloaded = await store.FindAsync(conversation.Id);

            Assert.Equal("已改名项目", renamed.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("已改名项目", reloaded!.ProjectId);
            Assert.False(Directory.Exists(Path.Combine(root, "待改名项目")));
            Assert.True(File.Exists(Path.Combine(root, "已改名项目", $"conversation-{conversation.Id}.md")));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task DeletingProjectRequiresItToBeEmptyAndNeverDeletesItsConversation()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("有会话项目");
            var conversation = await store.CreateConversationAsync(project.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteProjectAsync(project));

            Assert.NotNull(await store.FindAsync(conversation.Id));
            await store.DeleteConversationAsync(conversation);
            await store.DeleteProjectAsync(project);
            Assert.False(Directory.Exists(project.DirectoryPath));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task SystemProjectsCannotBeRenamedOrDeleted()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var inbox = (await store.GetProjectsAsync()).Single(project => project.Id == ConversationWorkspaceStore.InboxProjectId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameProjectAsync(inbox, "其他名称"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteProjectAsync(inbox));
            Assert.True(Directory.Exists(inbox.DirectoryPath));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task SettingsPersistCustomTurnLimitAndModelPriority()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            var expectedPriority = new[] { "深度思考", "Opus", "GPT 5.6 Think deeper" };

            await store.SaveAsync(new BridgeSettings
            {
                ConversationTurnLimit = 12,
                ModelPriority = ModelPriorityOptions.Serialize(expectedPriority)
            });
            var actual = await store.LoadAsync();

            Assert.Equal(12, actual.ConversationTurnLimit);
            Assert.Equal(expectedPriority, ModelPriorityOptions.Parse(actual.ModelPriority));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task ConfiguredTurnLimitOverridesTheModeDefault()
    {
        var runner = new CollaborationRunner(
            _ => throw new InvalidOperationException("must not execute"),
            configuredTurnLimit: 9);
        var context = new CollaborationContext(
            "request",
            CollaborationMode.Assist,
            9,
            "https://m365.cloud.microsoft/chat/",
            null,
            null);

        var exception = await Assert.ThrowsAsync<TurnBudgetExceededException>(() => runner.RunAsync(context));

        Assert.Contains("9-turn", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateWorkspaceRoot() => Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteWorkspaceRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
