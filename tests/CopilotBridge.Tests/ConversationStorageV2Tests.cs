using CopilotBridge.Core;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class ConversationStorageV2Tests
{
    [Fact]
    public async Task NewSaveSeparatesReadableMarkdownFromMetadataWithoutBodyDuplication()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var conversation = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId);
            conversation = conversation with
            {
                LocalTitle = "分离测试",
                Turns = [new ConversationTurn(DateTimeOffset.Now, "user", "BODY_ONLY_TOKEN")]
            };
            await store.SaveAsync(conversation);

            var markdownPath = Path.Combine(
                root,
                ConversationWorkspaceStore.StandaloneProjectId,
                $"conversation-{conversation.Id}.md");
            var sidecarPath = Path.Combine(root, ".bridge", "conversations", $"{conversation.Id}.json");
            var markdown = await File.ReadAllTextAsync(markdownPath);
            var sidecar = await File.ReadAllTextAsync(sidecarPath);
            var restored = await new ConversationWorkspaceStore(root).FindAsync(conversation.Id);

            Assert.StartsWith("# 分离测试", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("copilot-bridge-conversation:", markdown, StringComparison.Ordinal);
            Assert.Contains("copilot-bridge-turn:", markdown, StringComparison.Ordinal);
            Assert.Contains("BODY_ONLY_TOKEN", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("BODY_ONLY_TOKEN", sidecar, StringComparison.Ordinal);
            Assert.Contains("\"schema\": \"copilot-bridge-conversation/v2\"", sidecar, StringComparison.Ordinal);
            Assert.Equal("BODY_ONLY_TOKEN", Assert.Single(restored!.Turns).Markdown);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ExplicitMigrationBacksUpLegacyStorageAndCanRollbackUnchangedResult()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            await store.GetProjectsAsync();
            var conversation = new ConversationDocument
            {
                ProjectId = ConversationWorkspaceStore.StandaloneProjectId,
                LocalTitle = "旧格式",
                Turns = [new ConversationTurn(DateTimeOffset.Now, "copilot", "旧正文", "Opus", "verified")]
            };
            var markdownPath = Path.Combine(
                root,
                ConversationWorkspaceStore.StandaloneProjectId,
                $"conversation-{conversation.Id}.md");
            var legacy = store.Render(conversation);
            await File.WriteAllTextAsync(markdownPath, legacy);

            Assert.Equal(new ConversationStorageMigrationPreview(1, 0), await store.GetStorageMigrationPreviewAsync());
            var migrated = await store.MigrateStorageV2Async();
            var clean = await File.ReadAllTextAsync(markdownPath);
            var sidecarPath = Path.Combine(root, ".bridge", "conversations", $"{conversation.Id}.json");

            Assert.Equal(1, migrated.MigratedCount);
            Assert.NotNull(migrated.BackupDirectory);
            Assert.True(File.Exists(Path.Combine(migrated.BackupDirectory!, "manifest.json")));
            Assert.DoesNotContain("copilot-bridge-conversation:", clean, StringComparison.Ordinal);
            Assert.True(File.Exists(sidecarPath));
            Assert.Equal("旧正文", Assert.Single((await store.FindAsync(conversation.Id))!.Turns).Markdown);

            var rolledBack = await store.RollbackLatestStorageMigrationAsync();
            Assert.Equal(1, rolledBack.MigratedCount);
            Assert.Equal(legacy, await File.ReadAllTextAsync(markdownPath));
            Assert.False(File.Exists(sidecarPath));
            Assert.Equal(new ConversationStorageMigrationPreview(1, 0), await store.GetStorageMigrationPreviewAsync());
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ReadOnlyAuthorizedSearchDoesNotCreateSidecarsOrMigrateLegacyMarkdown()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("授权项目");
            project = await store.SetProjectAccessAsync(project, ConversationAccessLevel.Full);
            var conversation = new ConversationDocument
            {
                ProjectId = project.Id,
                LocalTitle = "只读旧会话",
                Turns = [new ConversationTurn(DateTimeOffset.Now, "user", "只读命中内容")]
            };
            var path = Path.Combine(project.DirectoryPath, $"conversation-{conversation.Id}.md");
            var legacy = store.Render(conversation);
            await File.WriteAllTextAsync(path, legacy);

            var results = await store.SearchAuthorizedConversationsAsync("只读命中");

            Assert.Single(results);
            Assert.Equal(legacy, await File.ReadAllTextAsync(path));
            Assert.False(Directory.Exists(Path.Combine(root, ".bridge")));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ReadUsesConsistentPendingSidecarWithoutWritingRecoveryFiles()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var conversation = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId);
            conversation = conversation with
            {
                Turns = [new ConversationTurn(DateTimeOffset.Now, "user", "pending 正文")]
            };
            await store.SaveAsync(conversation);
            var sidecar = Path.Combine(root, ".bridge", "conversations", $"{conversation.Id}.json");
            var pending = Path.Combine(root, ".bridge", "conversations", $"{conversation.Id}.pending.json");
            File.Move(sidecar, pending);

            var restored = await new ConversationWorkspaceStore(root).FindAsync(conversation.Id);

            Assert.Equal("pending 正文", Assert.Single(restored!.Turns).Markdown);
            Assert.False(File.Exists(sidecar));
            Assert.True(File.Exists(pending));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task RollbackRefusesToOverwriteConversationChangedAfterMigration()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            await store.GetProjectsAsync();
            var conversation = new ConversationDocument { LocalTitle = "旧格式" };
            var path = Path.Combine(
                root,
                ConversationWorkspaceStore.StandaloneProjectId,
                $"conversation-{conversation.Id}.md");
            await File.WriteAllTextAsync(path, store.Render(conversation));
            await store.MigrateStorageV2Async();
            var migrated = (await store.FindAsync(conversation.Id))! with { LocalTitle = "迁移后修改" };
            await store.SaveAsync(migrated);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RollbackLatestStorageMigrationAsync());
            Assert.Equal("迁移后修改", (await store.FindAsync(conversation.Id))!.LocalTitle);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task FindAsyncDoesNotCaptureABlockedSynchronizationContext()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var conversation = await store.CreateConversationAsync(
                ConversationWorkspaceStore.StandaloneProjectId,
                "UI deadlock regression");
            var completion = new TaskCompletionSource<ConversationDocument?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new BlockingSynchronizationContext());
                try
                {
                    completion.SetResult(store.FindAsync(conversation.Id).GetAwaiter().GetResult());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }) { IsBackground = true };

            thread.Start();
            var restored = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(restored);
            Assert.Equal(conversation.Id, restored!.Id);
        }
        finally { DeleteRoot(root); }
    }

    private sealed class BlockingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Intentionally does not pump callbacks, matching a blocked UI dispatcher.
        }
    }

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (!Directory.Exists(root)) return;
        File.SetAttributes(root, FileAttributes.Normal);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(directory, FileAttributes.Normal); }
            catch (IOException) { }
        }
        Directory.Delete(root, true);
    }
}
