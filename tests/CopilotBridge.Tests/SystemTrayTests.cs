using CopilotBridge.Core;
using CopilotBridge.UI;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class SystemTrayTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void ClosePolicyOnlyHidesEnabledOrdinaryWindowClose(
        bool enabled,
        bool explicitExit,
        bool sessionEnding,
        bool expected)
    {
        Assert.Equal(expected, TrayClosePolicy.ShouldHide(enabled, explicitExit, sessionEnding));
    }

    [Fact]
    public async Task LegacySettingsWithoutTrayOptionKeepExistingCloseBehavior()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "{}");

            var settings = await new SettingsStore(path).LoadAsync();

            Assert.False(settings.UseSystemTray);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
