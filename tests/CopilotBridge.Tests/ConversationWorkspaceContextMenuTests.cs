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
    public async Task UnclassifiedProjectCannotBeRenamedOrDeleted()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var unclassified = Assert.Single(await store.GetProjectsAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameProjectAsync(unclassified, "其他名称"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteProjectAsync(unclassified));
            Assert.True(Directory.Exists(unclassified.DirectoryPath));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task UnclassifiedConversationIsTheOnlyLockedSystemProject()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var projects = await store.GetProjectsAsync();
            var unclassified = Assert.Single(projects);

            Assert.Equal("未分类对话", ConversationWorkspaceStore.StandaloneProjectId);
            Assert.Equal(ConversationWorkspaceStore.StandaloneProjectId, unclassified.Id);
            Assert.True(unclassified.IsSystem);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenameProjectAsync(unclassified, "其他名称"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteProjectAsync(unclassified));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetProjectPinnedAsync(unclassified, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReorderProjectAsync(unclassified, unclassified));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task LegacyStandaloneFolderMigratesToUnclassifiedConversationProject()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var legacyDirectory = Path.Combine(root, "独立对话");
            Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllTextAsync(Path.Combine(legacyDirectory, ".bridge-project.md"), "# 独立对话");
            var store = new ConversationWorkspaceStore(root);
            var document = new ConversationDocument { ProjectId = "独立对话", LocalTitle = "旧会话" };
            await File.WriteAllTextAsync(
                Path.Combine(legacyDirectory, $"conversation-{document.Id}.md"),
                store.Render(document));

            var projects = await store.GetProjectsAsync();
            var restored = await store.FindAsync(document.Id);

            Assert.DoesNotContain(projects, project => project.Id == "独立对话");
            Assert.Contains(projects, project => project.Id == ConversationWorkspaceStore.StandaloneProjectId);
            Assert.False(Directory.Exists(legacyDirectory));
            Assert.NotNull(restored);
            Assert.Equal(ConversationWorkspaceStore.StandaloneProjectId, restored!.ProjectId);
            Assert.True(File.Exists(Path.Combine(
                root,
                ConversationWorkspaceStore.StandaloneProjectId,
                $"conversation-{document.Id}.md")));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task LegacyInboxConversationsMigrateIntoUnclassifiedAndInboxIsRemoved()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var inboxDirectory = Path.Combine(root, "收件箱");
            Directory.CreateDirectory(inboxDirectory);
            await File.WriteAllTextAsync(Path.Combine(inboxDirectory, ".bridge-project.md"), "# 收件箱");
            var store = new ConversationWorkspaceStore(root);
            var document = new ConversationDocument { ProjectId = "收件箱", LocalTitle = "旧收件箱会话" };
            await File.WriteAllTextAsync(
                Path.Combine(inboxDirectory, $"conversation-{document.Id}.md"),
                store.Render(document));

            var projects = await store.GetProjectsAsync();
            var restored = await store.FindAsync(document.Id);

            Assert.DoesNotContain(projects, project => project.Id == "收件箱");
            Assert.False(Directory.Exists(inboxDirectory));
            Assert.NotNull(restored);
            Assert.Equal(ConversationWorkspaceStore.StandaloneProjectId, restored!.ProjectId);
            Assert.True(File.Exists(Path.Combine(
                root,
                ConversationWorkspaceStore.StandaloneProjectId,
                $"conversation-{document.Id}.md")));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task CustomProjectReorderingPersistsWithoutMovingSystemProjects()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var alpha = await store.CreateProjectAsync("Alpha");
            var beta = await store.CreateProjectAsync("Beta");
            var gamma = await store.CreateProjectAsync("Gamma");

            await store.ReorderProjectAsync(gamma, alpha);
            var reloaded = await new ConversationWorkspaceStore(root).GetProjectsAsync();

            Assert.Equal(ConversationWorkspaceStore.StandaloneProjectId, reloaded[0].Id);
            Assert.Equal(new[] { gamma.Id, alpha.Id, beta.Id }, reloaded.Skip(1).Select(project => project.Id));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task PinningCustomProjectPersistsAndSortsItBeforeOtherCustomProjects()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var alpha = await store.CreateProjectAsync("Alpha");
            var zulu = await store.CreateProjectAsync("Zulu");

            var pinned = await store.SetProjectPinnedAsync(zulu, true);
            var reloaded = await new ConversationWorkspaceStore(root).GetProjectsAsync();

            Assert.True(pinned.IsPinned);
            Assert.True(reloaded.Single(project => project.Id == zulu.Id).IsPinned);
            Assert.True(reloaded[0].IsSystem);
            Assert.Equal(zulu.Id, reloaded[1].Id);
            Assert.Equal(alpha.Id, reloaded[2].Id);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task RenamingPinnedProjectKeepsItsPinnedState()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("待置顶项目");
            await store.SetProjectPinnedAsync(project, true);

            var renamed = await store.RenameProjectAsync(project, "已置顶项目");
            var reloaded = await new ConversationWorkspaceStore(root).GetProjectsAsync();

            Assert.True(renamed.IsPinned);
            Assert.True(reloaded.Single(candidate => candidate.Id == renamed.Id).IsPinned);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task SystemProjectsCannotBePinned()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var unclassified = (await store.GetProjectsAsync()).Single(project => project.Id == ConversationWorkspaceStore.StandaloneProjectId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetProjectPinnedAsync(unclassified, true));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task LegacyProjectMarkerLoadsAsUnpinned()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var legacyProjectPath = Path.Combine(root, "旧项目");
            Directory.CreateDirectory(legacyProjectPath);
            await File.WriteAllTextAsync(
                Path.Combine(legacyProjectPath, ".bridge-project.md"),
                "# 旧项目\n\nCopilot Bridge 项目目录。\n");

            var project = (await new ConversationWorkspaceStore(root).GetProjectsAsync())
                .Single(candidate => candidate.Id == "旧项目");

            Assert.False(project.IsPinned);
            Assert.Equal(ConversationAccessLevel.Off, project.AccessLevel);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task ProjectAccessDefaultsOffAndPersistsAcrossRename()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("待授权项目");

            Assert.Equal(ConversationAccessLevel.Off, project.AccessLevel);
            var authorized = await store.SetProjectAccessAsync(project, ConversationAccessLevel.Snippets);
            authorized = await store.SetProjectPinnedAsync(authorized, true);
            var renamed = await store.RenameProjectAsync(authorized, "已授权项目");
            var restored = (await new ConversationWorkspaceStore(root).GetProjectsAsync())
                .Single(candidate => candidate.Id == renamed.Id);

            Assert.Equal(ConversationAccessLevel.Snippets, authorized.AccessLevel);
            Assert.Equal(ConversationAccessLevel.Snippets, renamed.AccessLevel);
            Assert.Equal(ConversationAccessLevel.Snippets, restored.AccessLevel);
            Assert.True(restored.IsPinned);
            Assert.False(File.Exists(Path.Combine(restored.DirectoryPath, ".bridge-project.md.tmp")));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task UnclassifiedProjectAccessPersistsWhileSystemProtectionRemains()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var unclassified = Assert.Single(await store.GetProjectsAsync());

            var authorized = await store.SetProjectAccessAsync(unclassified, ConversationAccessLevel.Full);
            var restored = Assert.Single(await new ConversationWorkspaceStore(root).GetProjectsAsync());

            Assert.True(authorized.IsSystem);
            Assert.True(restored.IsSystem);
            Assert.False(restored.IsPinned);
            Assert.Equal(int.MaxValue, restored.SortOrder);
            Assert.Equal(ConversationAccessLevel.Full, restored.AccessLevel);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task InvalidProjectAccessMarkerFailsClosed()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var directory = Path.Combine(root, "异常权限项目");
            Directory.CreateDirectory(directory);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                "{\"isPinned\":false,\"sortOrder\":0,\"accessLevel\":99}"));
            await File.WriteAllTextAsync(
                Path.Combine(directory, ".bridge-project.md"),
                $"<!-- copilot-bridge-project:{encoded} -->\n\n# 异常权限项目\n");

            var project = (await new ConversationWorkspaceStore(root).GetProjectsAsync())
                .Single(candidate => candidate.Id == "异常权限项目");

            Assert.Equal(ConversationAccessLevel.Off, project.AccessLevel);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task AuthorizedSearchSeparatesMetadataSnippetsAndOffProjects()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var metadataProject = await store.CreateProjectAsync("元数据项目");
            var snippetsProject = await store.CreateProjectAsync("片段项目");
            var offProject = await store.CreateProjectAsync("关闭项目");
            await store.SetProjectAccessAsync(metadataProject, ConversationAccessLevel.Metadata);
            snippetsProject = await store.SetProjectAccessAsync(snippetsProject, ConversationAccessLevel.Snippets);
            var snippetsMarker = Path.Combine(snippetsProject.DirectoryPath, ".bridge-project.md");
            var markerBeforeSearch = await File.ReadAllTextAsync(snippetsMarker);

            var metadataConversation = await store.CreateConversationAsync(metadataProject.Id, "元数据标题");
            await store.SaveAsync(metadataConversation with
            {
                Turns = [new ConversationTurn(DateTimeOffset.Now, "user", "正文 needle 不应被元数据权限命中")]
            });
            var snippetsConversation = await store.CreateConversationAsync(snippetsProject.Id, "普通标题");
            await store.SaveAsync(snippetsConversation with
            {
                Turns = [new ConversationTurn(DateTimeOffset.Now, "copilot", "可检索的 needle 正文")]
            });
            var offConversation = await store.CreateConversationAsync(offProject.Id, "关闭标题");
            await store.SaveAsync(offConversation with
            {
                Turns = [new ConversationTurn(DateTimeOffset.Now, "copilot", "关闭项目 needle")]
            });

            var contentMatches = await store.SearchAuthorizedConversationsAsync("needle");
            var metadataMatches = await store.SearchAuthorizedConversationsAsync("元数据标题");

            var content = Assert.Single(contentMatches);
            Assert.Equal(snippetsConversation.Id, content.ConversationId);
            Assert.Equal("content", content.MatchScope);
            Assert.Equal("copilot", content.MatchRole);
            Assert.Contains("needle", content.Snippet, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(metadataConversation.Id, Assert.Single(metadataMatches).ConversationId);
            Assert.DoesNotContain(contentMatches, result => result.ConversationId == offConversation.Id);
            Assert.Equal(markerBeforeSearch, await File.ReadAllTextAsync(snippetsMarker));

            await store.SetProjectAccessAsync(snippetsProject, ConversationAccessLevel.Off);
            Assert.Empty(await store.SearchAuthorizedConversationsAsync("needle"));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task AuthorizedSearchDoesNotCreateAMissingWorkspace()
    {
        var root = CreateWorkspaceRoot();
        var store = new ConversationWorkspaceStore(root);

        Assert.Empty(await store.SearchAuthorizedConversationsAsync());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task FullAccessReadsOnlyTheRequestedTurnPageAndDowngradeBlocksImmediately()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("分页项目");
            project = await store.SetProjectAccessAsync(project, ConversationAccessLevel.Snippets);
            var conversation = await store.CreateConversationAsync(project.Id, "分页会话");
            await store.SaveAsync(conversation with
            {
                Turns =
                [
                    new ConversationTurn(DateTimeOffset.Now, "user", "第一轮"),
                    new ConversationTurn(DateTimeOffset.Now, "copilot", "第二轮", "Opus", "verified"),
                    new ConversationTurn(DateTimeOffset.Now, "user", "第三轮")
                ]
            });

            var blocked = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
                store.ReadAuthorizedConversationAsync(conversation.Id));
            Assert.Equal("conversation_not_accessible", blocked.ErrorCode);

            project = await store.SetProjectAccessAsync(project, ConversationAccessLevel.Full);
            var page = await store.ReadAuthorizedConversationAsync(conversation.Id, startTurn: 1, maxTurns: 1);

            Assert.Equal(1, page.StartTurn);
            Assert.Equal(3, page.TotalTurns);
            Assert.True(page.HasMore);
            Assert.Equal("第二轮", Assert.Single(page.Turns).Markdown);

            await store.SetProjectAccessAsync(project, ConversationAccessLevel.Metadata);
            blocked = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
                store.ReadAuthorizedConversationAsync(conversation.Id));
            Assert.Equal("conversation_not_accessible", blocked.ErrorCode);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task ExplicitUnauthorizedProjectDoesNotRevealWhetherItExists()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("未授权项目");

            var existing = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
                store.SearchAuthorizedConversationsAsync(projectId: project.Id));
            var missing = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
                store.SearchAuthorizedConversationsAsync(projectId: "不存在项目"));

            Assert.Equal("project_not_accessible", existing.ErrorCode);
            Assert.Equal(existing.ErrorCode, missing.ErrorCode);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public void ShortcutManagerCreatesIdempotentStartAndDesktopLinks()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var programs = Path.Combine(root, "Programs");
            var desktop = Path.Combine(root, "Desktop");
            var manager = new ShortcutManager(Environment.ProcessPath!, programs, desktop);

            var startPath = manager.CreateStartMenuShortcut();
            var desktopPath = manager.CreateDesktopShortcut();
            manager.CreateStartMenuShortcut();

            Assert.Equal(Path.Combine(programs, "Copilot Bridge.lnk"), startPath);
            Assert.Equal(Path.Combine(desktop, "Copilot Bridge.lnk"), desktopPath);
            Assert.True(File.Exists(startPath));
            Assert.True(File.Exists(desktopPath));
            Assert.True(new FileInfo(startPath).Length > 0);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task SettingsPersistModelPriorityAndCollaborationBudgets()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            var expectedPriority = new[] { "深度思考", "Opus", "GPT 5.6 Think deeper" };

            await store.SaveAsync(new BridgeSettings
            {
                ModelPriority = ModelPriorityOptions.Serialize(expectedPriority),
                AssistTurnBudget = 4,
                OutsourceTurnBudget = 9,
                ReviewTurnBudget = 3
            });
            var actual = await store.LoadAsync();

            Assert.Equal(expectedPriority, ModelPriorityOptions.Parse(actual.ModelPriority));
            Assert.Equal(4, actual.AssistTurnBudget);
            Assert.Equal(9, actual.OutsourceTurnBudget);
            Assert.Equal(3, actual.ReviewTurnBudget);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Fact]
    public async Task ExistingSettingsTurnLimitIsIgnoredForBackwardCompatibility()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var path = Path.Combine(root, "settings.json");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, "{\"conversationTurnLimit\":20}");

            var settings = await new SettingsStore(path).LoadAsync();
            var saved = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("conversationTurnLimit", saved, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(CollaborationBudgetOptions.DefaultAssist, settings.AssistTurnBudget);
            Assert.Equal(CollaborationBudgetOptions.DefaultOutsource, settings.OutsourceTurnBudget);
            Assert.Equal(CollaborationBudgetOptions.DefaultReview, settings.ReviewTurnBudget);
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    [Theory]
    [InlineData(0, 6, 1)]
    [InlineData(2, 21, 1)]
    [InlineData(2, 6, -1)]
    public async Task SettingsRejectCollaborationBudgetsOutsideOneThroughTwenty(
        int assist,
        int outsource,
        int review)
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new BridgeSettings
            {
                AssistTurnBudget = assist,
                OutsourceTurnBudget = outsource,
                ReviewTurnBudget = review
            }));
        }
        finally { DeleteWorkspaceRoot(root); }
    }

    private static string CreateWorkspaceRoot() => Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteWorkspaceRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
