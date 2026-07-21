using CopilotBridge.Core;
using CopilotBridge.Mcp;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class ConsultRetryContractTests
{
    [Fact]
    public async Task FreshPreSubmitFailureRequiresNewConsultationRetry()
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "CopilotBridge.Tests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        await using var tools = new CopilotBridgeTools(new SettingsStore(settingsPath));

        var response = await tools.ConsultAsync("", "user_explicit");

        Assert.Equal("not_submitted", response.Status);
        Assert.Equal("invalid_request", response.ErrorCode);
        Assert.True(response.CanRetrySafely);
        Assert.Equal("new_consultation", response.RetryAction);
    }

    [Theory]
    [InlineData(false, true, "none")]
    [InlineData(false, false, "none")]
    [InlineData(true, true, "new_consultation")]
    [InlineData(true, false, "reuse_consultation")]
    public void RetryActionDistinguishesFreshExistingAndUnsafeFailures(
        bool canRetrySafely,
        bool startFresh,
        string expected)
    {
        Assert.Equal(expected, CopilotBridgeTools.RetryActionFor(canRetrySafely, startFresh));
    }
}
