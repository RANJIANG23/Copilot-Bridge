using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class UiThemeResourceTests
{
    [Fact]
    public void MainWindowUsesOneSharedThemeDictionary()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml"));
        var resources = document.Descendants().Single(element => element.Name.LocalName == "Window.Resources");

        var sources = resources
            .Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => (string?)element.Attribute("Source"))
            .OfType<string>()
            .ToArray();

        Assert.Single(sources);
        Assert.Equal("Theme/CopilotTheme.xaml", sources[0]);
        Assert.DoesNotContain(resources.Descendants(), element => element.Name.LocalName == "Style");
    }

    [Fact]
    public void ThemeDictionaryContainsEveryRuntimePaletteKeyAndCommonStates()
    {
        var root = FindRepositoryRoot();
        var themePath = Path.Combine(root, "src", "CopilotBridge", "UI", "Theme", "CopilotTheme.xaml");
        var themeText = File.ReadAllText(themePath);
        var theme = XDocument.Load(themePath);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = theme.Root!
            .DescendantsAndSelf()
            .Select(element => (string?)element.Attribute(xaml + "Key"))
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(keys.Count, theme.Root!.DescendantsAndSelf().Count(element => element.Attribute(xaml + "Key") is not null));

        var code = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));
        var runtimePaletteKeys = Regex.Matches(code, "SetThemeBrush\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);
        foreach (var key in runtimePaletteKeys)
        {
            Assert.Contains(key, keys);
        }

        Assert.Contains("ControlCornerRadius", keys);
        Assert.Contains("FocusBorderThickness", keys);
        Assert.Contains("Property=\"IsMouseOver\"", themeText);
        Assert.Contains("Property=\"IsPressed\"", themeText);
        Assert.Contains("Property=\"IsKeyboardFocused\"", themeText);
        Assert.Contains("Property=\"IsEnabled\"", themeText);
    }

    [Fact]
    public void AccessibleListsExposeKeyboardAlternativesAndLiveStatus()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));
        var layout = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "DesktopLayout.cs"));

        Assert.Contains("PreviewKeyDown=\"ProjectListBox_PreviewKeyDown\"", xaml);
        Assert.Contains("PreviewKeyDown=\"ModelPriorityListBox_PreviewKeyDown\"", xaml);
        Assert.Contains("AutomationProperties.HelpText=\"按 Alt+上移或 Alt+下移调整所选项目顺序\"", xaml);
        Assert.Contains("AutomationProperties.HelpText=\"按 Alt+上移或 Alt+下移调整所选模型顺序\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.True(Regex.Matches(xaml, "AutomationProperties.Name=").Count >= 12);
        Assert.Contains("MinWidth=\"1080\"", xaml);
        Assert.Contains("MinHeight=\"700\"", xaml);
        Assert.Contains("DesktopLayout.ForWidth(ActualWidth)", code);
        Assert.Contains("width < 1180", layout);
        Assert.Contains("KeyboardMoveDirection", code);
        Assert.Contains("ReorderProjectAsync", code);
        Assert.Contains("MoveModelPriorityToIndex", code);
    }

    [Fact]
    public void NeutralCollaborationSettingsRemainReachableAtMinimumWindowSize()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var window = Assert.IsType<XElement>(document.Root);
        var collaborationPanel = document
            .Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "CollaborationPanel");
        var policyComboBox = collaborationPanel
            .Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "PolicyComboBox");

        Assert.Equal("1080", (string?)window.Attribute("MinWidth"));
        Assert.Equal("700", (string?)window.Attribute("MinHeight"));
        Assert.Contains(
            collaborationPanel.Descendants(),
            element => element.Name.LocalName == "ScrollViewer" &&
                (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
        Assert.Contains(
            policyComboBox.Elements(),
            element => (string?)element.Attribute("Content") == "Agent 可自动征询");
        Assert.DoesNotContain(
            policyComboBox.Elements(),
            element => (string?)element.Attribute("Content") == "Codex 可自动征询");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBridge.sln"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
