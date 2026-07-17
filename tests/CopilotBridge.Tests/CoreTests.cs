using CopilotBridge.Browser;
using CopilotBridge.Core;
using Microsoft.Playwright;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class CoreTests
{
    private static readonly ProviderSelectors Selectors = ProviderSelectors.Load();

    [Theory]
    [InlineData("model-delayed-opus.html", "Opus")]
    [InlineData("model-opus-disabled.html", "GPT 5.6 Think deeper")]
    [InlineData("model-gpt-versions.html", "GPT 5.6 Think deeper")]
    [InlineData("model-deep-only.html", "深度思考")]
    public async Task ModelQueueSelectsHighestAllowedFixture(string fixture, string expected)
    {
        await using var browser = await FixtureBrowser.OpenAsync(fixture);
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        var selected = await driver.SelectAllowedModelAsync();

        Assert.Equal(expected, selected);
        Assert.DoesNotContain("GPT 5.5", await browser.Page.Locator("#gptModeSwitcher").InnerTextAsync());
        Assert.DoesNotContain("快速", await browser.Page.Locator("#gptModeSwitcher").InnerTextAsync());
    }

    [Fact]
    public async Task ForbiddenOnlyMenuSendsZeroTimes()
    {
        await using var browser = await FixtureBrowser.OpenAsync("model-forbidden-only.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SelectAllowedModelAsync());

        Assert.Equal(0, await browser.Page.EvaluateAsync<int>("window.sendCount"));
    }

    [Fact]
    public async Task CompletedReplyIsExtractedAsMarkdownAfterOneSend()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-success.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings(replyTimeoutSeconds: 2));

        var result = await driver.SendAndReadAsync("fixture prompt");

        Assert.Equal(1, result.UserMessageDelta);
        Assert.Equal(1, result.AssistantMessageDelta);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
        Assert.Contains("## Answer", result.ReplyMarkdown);
        Assert.Contains("[docs](https://example.com/docs)", result.ReplyMarkdown);
        Assert.Contains("- First", result.ReplyMarkdown);
        Assert.Contains("```csharp", result.ReplyMarkdown);
        Assert.Contains("| Name | Value |", result.ReplyMarkdown);
    }

    [Fact]
    public async Task VirtualizedMessageListStillVerifiesExactlyOneTurn()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-virtualized.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings(replyTimeoutSeconds: 2));

        var result = await driver.SendAndReadAsync("unique virtualized prompt");

        Assert.Equal(1, result.UserMessageDelta);
        Assert.Equal(1, result.AssistantMessageDelta);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
        Assert.Equal("virtualized reply", result.ReplyMarkdown);
    }

    [Fact]
    public async Task VerifiedSendTimeoutBecomesReplyTimeoutWithoutRetry()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-timeout.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        var exception = await Assert.ThrowsAsync<ReplyTimeoutException>(
            () => driver.SendAndReadAsync("timeout prompt"));

        Assert.Contains("message was submitted", exception.Message);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
    }

    [Fact]
    public async Task PageErrorBecomesSubmissionUnknownWithoutRetry()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-page-error.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        var exception = await Assert.ThrowsAsync<SubmissionUnknownException>(
            () => driver.SendAndReadAsync("error prompt"));

        Assert.Contains("Copilot page error", exception.Message);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
    }

    [Fact]
    public async Task PreClickReadbackMismatchClearsComposerAndSendsZeroTimes()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-readback-mismatch.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SendAndReadAsync("exact prompt"));

        Assert.Equal(0, await browser.Page.EvaluateAsync<int>("window.sendCount"));
        Assert.Equal(string.Empty, await browser.Page.Locator("textarea").InputValueAsync());
    }

    [Fact]
    public async Task MarkdownExtractorPreservesRequiredStructures()
    {
        await using var browser = await FixtureBrowser.OpenAsync("markdown.html");

        var markdown = await RenderedMarkdownExtractor.ExtractAsync(browser.Page.Locator("#reply"));

        Assert.Contains("# Title", markdown);
        Assert.Contains("[guide](https://example.com/guide)", markdown);
        Assert.Contains("`run()`", markdown);
        Assert.Contains("1. Alpha", markdown);
        Assert.Contains("```json", markdown);
        Assert.Contains("| Key | Value |", markdown);
        Assert.Contains("| --- | --- |", markdown);
    }

    [Fact]
    public async Task SettingsStoreRoundTripsWithAtomicReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        var expected = new BridgeSettings
        {
            EdgeUserDataDirectory = @"C:\EdgeData",
            MenuMinimumWaitMilliseconds = 25,
            MenuMaximumWaitMilliseconds = 250,
            ReplyTimeoutSeconds = 42
        };

        try
        {
            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, false);
            }
        }
    }

    [Fact]
    public async Task CoordinatorReusesValidatedConversationUrlForFollowup()
    {
        await using var browser = await FixtureBrowser.OpenAsync("assist-followup.html");
        var html = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "assist-followup.html"));
        const string conversationUrl = "https://m365.cloud.microsoft/chat/conversation/followup-test";
        await browser.Page.RouteAsync(
            conversationUrl,
            route => route.FulfillAsync(new RouteFulfillOptions
            {
                Body = html,
                ContentType = "text/html"
            }));
        var coordinator = new ConsultationCoordinator(FastSettings(replyTimeoutSeconds: 2), Selectors);

        var result = await coordinator.AssistOnPageAsync(
            browser.Page,
            new AssistRequest("followup prompt", conversationUrl));

        Assert.Equal("Opus", result.Model);
        Assert.Equal("followup ok", result.ReplyMarkdown);
        Assert.Equal(conversationUrl, result.ConversationUrl);
        Assert.Equal(1, result.UserMessageDelta);
        Assert.Equal(1, result.AssistantMessageDelta);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
    }

    [Fact]
    public void EndpointResolverUsesProtectedWebSocketPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "DevToolsActivePort");

        try
        {
            File.WriteAllText(path, "9222\n/devtools/browser/test-id\n");
            Assert.Equal(
                "ws://127.0.0.1:9222/devtools/browser/test-id",
                EdgeSessionAdapter.ResolveEndpoint(directory));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Directory.Delete(directory, false);
        }
    }

    [Theory]
    [InlineData((int)ConsultationPolicy.ManualOnly, "user_explicit", null)]
    [InlineData((int)ConsultationPolicy.ManualOnly, "codex_auto", "blocked_by_policy")]
    [InlineData((int)ConsultationPolicy.CodexMayConsult, "codex_auto", null)]
    [InlineData((int)ConsultationPolicy.CodexMayConsult, "required_checkpoint", null)]
    [InlineData((int)ConsultationPolicy.RequiredForKeyDesign, "required_checkpoint", null)]
    [InlineData((int)ConsultationPolicy.Disabled, "user_explicit", "blocked_by_policy")]
    public void ConsultationPolicyEnforcesTrigger(
        int policy,
        string trigger,
        string? expectedError)
    {
        var settings = new BridgeSettings { ConsultationPolicy = (ConsultationPolicy)policy };

        Assert.Equal(expectedError, Mcp.CopilotBridgeTools.ValidatePolicy(settings, trigger));
    }

    [Fact]
    public void ConsultationLeaseReturnsBusyWithoutQueueing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "CopilotBridge.Tests",
            Guid.NewGuid().ToString("N"),
            "consultation.lock");

        Assert.False(ConsultationLease.IsBusy(path));
        Assert.False(File.Exists(path));
        using var first = ConsultationLease.TryAcquire(path);
        using var second = ConsultationLease.TryAcquire(path);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(ConsultationLease.IsBusy(path));
    }

    [Fact]
    public async Task ConsultationStateStoresOnlyIdAndConversationUrl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "consultations.json");
        var store = new ConsultationStateStore(path);

        try
        {
            await store.SaveConversationAsync("consultation-a", "https://m365.cloud.microsoft/chat/a");

            Assert.Equal(
                "https://m365.cloud.microsoft/chat/a",
                await store.FindConversationAsync("consultation-a"));
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reply", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void DiagnosticLogRecordsBoundedErrorMetadataWithoutMultilinePayload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "bridge.log");

        try
        {
            DiagnosticLog.Write(
                "submission_unknown",
                new SubmissionUnknownException(
                    "single-line boundary",
                    new TimeoutException("user message was not observed\r\nafter click")),
                path);

            var lines = File.ReadAllLines(path);
            var line = Assert.Single(lines);
            Assert.Contains("[submission_unknown]", line);
            Assert.Contains("SubmissionUnknownException", line);
            Assert.Contains("TimeoutException", line);
            Assert.Contains("after click", line);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static BridgeSettings FastSettings(int replyTimeoutSeconds = 1) => new()
    {
        MenuMinimumWaitMilliseconds = 10,
        MenuMaximumWaitMilliseconds = 200,
        ReplyTimeoutSeconds = replyTimeoutSeconds
    };
}

internal sealed class FixtureBrowser : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private FixtureBrowser(IPlaywright playwright, IBrowser browser, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        Page = page;
    }

    internal IPage Page { get; }

    internal static async Task<FixtureBrowser> OpenAsync(string fixtureName)
    {
        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft",
            "Edge",
            "Application",
            "msedge.exe");
        if (!File.Exists(edgePath))
        {
            throw new FileNotFoundException("Microsoft Edge is required for DOM fixture tests.", edgePath);
        }

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var html = await File.ReadAllTextAsync(fixturePath);
        var playwright = await Playwright.CreateAsync();

        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                ExecutablePath = edgePath,
                Headless = true
            });
            var page = await browser.NewPageAsync();
            await page.SetContentAsync(html);
            return new FixtureBrowser(playwright, browser, page);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
