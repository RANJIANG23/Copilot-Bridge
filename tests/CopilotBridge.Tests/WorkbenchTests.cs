using CopilotBridge.Core;
using CopilotBridge.UI;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class WorkbenchTests
{
    [Fact]
    public async Task TagsAreNormalizedStoredOutsideMarkdownAndIncludedInAuthorizedMetadataSearch()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var project = await store.CreateProjectAsync("标签项目");
            project = await store.SetProjectAccessAsync(project, ConversationAccessLevel.Metadata);
            var conversation = await store.CreateConversationAsync(project.Id, "标签会话");

            conversation = await store.UpdateTagsAsync(
                conversation,
                [" 设计 ", "Design", "设计", "", "验收"]);

            var markdownPath = Path.Combine(
                project.DirectoryPath,
                $"conversation-{conversation.Id}.md");
            var sidecarPath = Path.Combine(
                root,
                ".bridge",
                "conversations",
                $"{conversation.Id}.json");
            var restored = await new ConversationWorkspaceStore(root).FindAsync(conversation.Id);
            var matches = await store.SearchAuthorizedConversationsAsync("验收");

            Assert.Equal(["设计", "Design", "验收"], restored!.Tags);
            Assert.DoesNotContain("验收", await File.ReadAllTextAsync(markdownPath), StringComparison.Ordinal);
            Assert.Contains("\"tags\"", await File.ReadAllTextAsync(sidecarPath), StringComparison.Ordinal);
            Assert.Equal("metadata", Assert.Single(matches).MatchScope);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task OldV2SidecarWithoutTagsLoadsAsEmptyTags()
    {
        var root = CreateRoot();
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var conversation = await store.CreateConversationAsync(
                ConversationWorkspaceStore.StandaloneProjectId,
                "兼容会话");
            var sidecarPath = Path.Combine(root, ".bridge", "conversations", $"{conversation.Id}.json");
            var sidecar = await File.ReadAllTextAsync(sidecarPath);
            var tagsStart = sidecar.IndexOf("  \"tags\":", StringComparison.Ordinal);
            Assert.True(tagsStart >= 0);
            var tagsEnd = sidecar.IndexOf(Environment.NewLine, tagsStart, StringComparison.Ordinal);
            sidecar = sidecar.Remove(tagsStart, tagsEnd - tagsStart + Environment.NewLine.Length);
            await File.WriteAllTextAsync(sidecarPath, sidecar);

            var restored = await new ConversationWorkspaceStore(root).FindAsync(conversation.Id);

            Assert.NotNull(restored);
            Assert.Empty(restored!.Tags);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task PromptTemplatesRoundTripRenameRejectDuplicatesAndDeleteAtomically()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "prompt-templates.json");
            var store = new PromptTemplateStore(path);

            var first = await store.SaveAsync(null, "审核", "检查边界");
            await store.SaveAsync(null, "总结", "形成摘要");
            var renamed = await store.SaveAsync(first.Id, "架构审核", first.Content);

            Assert.Equal(first.Id, renamed.Id);
            Assert.Equal(["架构审核", "总结"], (await store.LoadAsync()).Select(item => item.Name));
            await Assert.ThrowsAsync<InvalidDataException>(
                () => store.SaveAsync(first.Id, "总结", "冲突内容"));

            await store.DeleteAsync(first.Id);

            Assert.Equal("总结", Assert.Single(await store.LoadAsync()).Name);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void TagsRejectOversizedOrExcessiveValues()
    {
        Assert.Throws<InvalidDataException>(() =>
            ConversationWorkspaceStore.NormalizeTags([new string('x', 33)]));
        Assert.Throws<InvalidDataException>(() =>
            ConversationWorkspaceStore.NormalizeTags(
                Enumerable.Range(1, 13).Select(index => $"tag-{index}")));
    }

    [Fact]
    public async Task DesktopReadinessDefaultsAndSettingsRoundTripStayOptIn()
    {
        var root = CreateRoot();
        try
        {
            var defaults = new BridgeSettings();
            Assert.False(defaults.StartWithWindows);
            Assert.False(defaults.OnboardingCompleted);

            var path = Path.Combine(root, "settings.json");
            var store = new SettingsStore(path);
            await store.SaveAsync(defaults with
            {
                StartWithWindows = true,
                OnboardingCompleted = true
            });
            var restored = await store.LoadAsync();

            Assert.True(restored.StartWithWindows);
            Assert.True(restored.OnboardingCompleted);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void StartupShortcutIsCurrentUserScopedReversibleAndIdempotent()
    {
        var root = CreateRoot();
        try
        {
            var startup = Path.Combine(root, "Startup");
            var manager = new ShortcutManager(
                Environment.ProcessPath!,
                Path.Combine(root, "Programs"),
                Path.Combine(root, "Desktop"),
                startup);

            Assert.False(manager.IsStartupEnabled);
            manager.SetStartupEnabled(true);
            manager.SetStartupEnabled(true);
            Assert.True(manager.IsStartupEnabled);
            Assert.Equal(Path.Combine(startup, "Copilot Bridge.lnk"), manager.StartupShortcutPath);

            manager.SetStartupEnabled(false);
            manager.SetStartupEnabled(false);
            Assert.False(manager.IsStartupEnabled);
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData(1080, 220, 160, 190, false)]
    [InlineData(1179, 220, 160, 190, false)]
    [InlineData(1180, 264, 220, 280, true)]
    [InlineData(1400, 264, 220, 280, true)]
    public void DesktopLayoutUsesOneBoundedResponsiveThreshold(
        double width,
        double sidebar,
        double projects,
        double conversations,
        bool showSecondary)
    {
        var layout = DesktopLayout.ForWidth(width);

        Assert.Equal(sidebar, layout.SidebarWidth);
        Assert.Equal(projects, layout.ProjectColumnWidth);
        Assert.Equal(conversations, layout.ConversationColumnWidth);
        Assert.Equal(showSecondary, layout.ShowSecondarySidebarText);
    }

    [Fact]
    public void MainWindowHonorsRuntimeWindowsHighContrastWithoutAddingAThirdTheme()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "Core", "BridgeSettings.cs"));

        Assert.Contains("SystemParameters.HighContrast", source, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.StaticPropertyChanged", source, StringComparison.Ordinal);
        Assert.Contains("SystemColors.HighlightBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HighContrast,", settings, StringComparison.Ordinal);
    }

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(),
        "CopilotBridge.Tests",
        Guid.NewGuid().ToString("N"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBridge.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

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
