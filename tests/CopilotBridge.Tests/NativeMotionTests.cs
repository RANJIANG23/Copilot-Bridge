using System.Xml.Linq;
using CopilotBridge.UI;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class NativeMotionTests
{
    [Fact]
    public void PolicyMirrorsWindowsClientAreaAnimationSetting()
    {
        Assert.True(NativeMotionPolicy.IsEnabledFor(true));
        Assert.False(NativeMotionPolicy.IsEnabledFor(false));
    }

    [Fact]
    public void EveryMotionDurationStaysInsideFrozenRange()
    {
        int[] durations =
        [
            NativeMotionPolicy.ButtonPressDurationMs,
            NativeMotionPolicy.ButtonHoverDurationMs,
            NativeMotionPolicy.ButtonReleaseDurationMs,
            NativeMotionPolicy.ToggleDurationMs,
            NativeMotionPolicy.NoticeDurationMs
        ];

        Assert.All(durations, duration => Assert.InRange(duration, 90, 190));
        Assert.Equal(20, NativeMotionPolicy.ToggleTravel);
    }

    [Fact]
    public void ToggleMotionUsesRenderTranslationInsteadOfLayoutAlignment()
    {
        var root = FindRepositoryRoot();
        var themePath = Path.Combine(root, "src", "CopilotBridge", "UI", "Theme", "CopilotTheme.xaml");
        var theme = XDocument.Load(themePath);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var thumb = theme.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "Thumb");

        Assert.Contains(thumb.Descendants(), element => element.Name.LocalName == "TranslateTransform");
        Assert.DoesNotContain(theme.Descendants().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "Thumb" &&
            (string?)setter.Attribute("Property") == "HorizontalAlignment");
    }

    [Fact]
    public void MotionCodeUsesOnlyRenderPropertiesAndSystemGate()
    {
        var root = FindRepositoryRoot();
        var motion = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.Motion.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));

        Assert.Contains("SystemParameters.ClientAreaAnimation", motion);
        Assert.Contains("OpacityProperty", motion);
        Assert.Contains("ScaleTransform.ScaleXProperty", motion);
        Assert.Contains("TranslateTransform.XProperty", motion);
        Assert.DoesNotContain("WidthProperty", motion);
        Assert.DoesNotContain("HeightProperty", motion);
        Assert.DoesNotContain("MarginProperty", motion);
        Assert.Contains("AnimateMotionValue(container, OpacityProperty", window);
        Assert.Contains("AnimateMotionFrom(listBox, OpacityProperty", window);
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
