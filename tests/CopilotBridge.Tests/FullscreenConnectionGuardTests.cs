using CopilotBridge.Browser;
using CopilotBridge.Core;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class FullscreenConnectionGuardTests
{
    private static readonly ForegroundWindowSnapshot FullscreenSnapshot = new(
        new ScreenBounds(-1920, 0, 0, 1080),
        new ScreenBounds(-1920, 0, 0, 1080));

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(-8, -8, 1928, 1088, true)]
    [InlineData(0, 0, 1920, 1040, false)]
    [InlineData(120, 80, 1800, 1000, false)]
    [InlineData(-1920, 0, 0, 1080, true)]
    public void CoverageUsesMonitorGeometryWithoutTitlesOrProcessNames(
        int left,
        int top,
        int right,
        int bottom,
        bool expected)
    {
        var monitor = right <= 0
            ? new ScreenBounds(-1920, 0, 0, 1080)
            : new ScreenBounds(0, 0, 1920, 1080);

        Assert.Equal(
            expected,
            FullscreenConnectionGuard.CoversMonitor(
                new ScreenBounds(left, top, right, bottom),
                monitor));
    }

    [Fact]
    public void ProtectionDefaultsOnAndCanBeDisabledOrExplicitlyBypassed()
    {
        Assert.True(new BridgeSettings().FullscreenProtectionEnabled);
        Assert.Throws<FullscreenProtectionException>(() =>
            FullscreenConnectionGuard.ThrowIfBlocked(
                new BridgeSettings(),
                snapshotProvider: () => FullscreenSnapshot));

        FullscreenConnectionGuard.ThrowIfBlocked(
            new BridgeSettings { FullscreenProtectionEnabled = false },
            snapshotProvider: () => FullscreenSnapshot);
        FullscreenConnectionGuard.ThrowIfBlocked(
            new BridgeSettings(),
            bypassProtection: true,
            snapshotProvider: () => FullscreenSnapshot);
    }

    [Fact]
    public async Task GuardStopsBeforeEndpointResolutionOrCdpConnection()
    {
        var settings = new BridgeSettings
        {
            EdgeUserDataDirectory = Path.Combine(
                Path.GetTempPath(),
                "CopilotBridge.Tests",
                Guid.NewGuid().ToString("N"))
        };

        await Assert.ThrowsAsync<FullscreenProtectionException>(() =>
            EdgeSessionAdapter.ConnectAsync(
                settings,
                ProviderSelectors.Load(),
                timeoutMilliseconds: 1,
                fullscreenSnapshotProvider: () => FullscreenSnapshot));
    }

    [Fact]
    public void SourceKeepsAutomaticAndMcpColdConnectionsProtected()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(
            Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));
        var tools = File.ReadAllText(
            Path.Combine(root, "src", "CopilotBridge", "Mcp", "CopilotBridgeTools.cs"));
        var guard = File.ReadAllText(
            Path.Combine(root, "src", "CopilotBridge", "Browser", "FullscreenConnectionGuard.cs"));

        Assert.Contains(
            "GetSessionAsync(bypassFullscreenProtection: !automatic)",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "bypassFullscreenProtection: true",
            tools,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetWindowText", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessName", guard, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CopilotBridge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
