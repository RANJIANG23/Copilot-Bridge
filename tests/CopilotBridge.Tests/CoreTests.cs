using CopilotBridge.Browser;
using CopilotBridge.Core;
using CopilotBridge.UI;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Text.Json;
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
    [InlineData("model-delayed-switcher.html", "Opus")]
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
    public async Task LoginRequiredFailsBeforeModelSelectionOrSend()
    {
        await using var browser = await FixtureBrowser.OpenAsync("login-required.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.SelectAllowedModelAsync());

        Assert.Contains("login is required", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task NestedLiveMessageMarkupCountsOneLogicalTurn()
    {
        await using var browser = await FixtureBrowser.OpenAsync("send-nested-messages.html");
        var driver = new CopilotPageDriver(browser.Page, Selectors, FastSettings(replyTimeoutSeconds: 2));

        var result = await driver.SendAndReadAsync("nested prompt");

        Assert.Equal(1, result.UserMessageDelta);
        Assert.Equal(1, result.AssistantMessageDelta);
        Assert.Equal(1, await browser.Page.EvaluateAsync<int>("window.sendCount"));
        Assert.Equal("nested reply", result.ReplyMarkdown);
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
            ReplyTimeoutSeconds = 42,
            DisplayLanguage = AppLanguage.English,
            Theme = AppTheme.Dark,
            KeepMcpRunningInBackground = false
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
    public void BackgroundResidentDefaultsToEnabled()
    {
        Assert.True(new BridgeSettings().KeepMcpRunningInBackground);
    }

    [Fact]
    public void McpProcessRegistrationRoundTripsAndCanBeRemoved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var registryPath = Path.Combine(directory, "mcp-processes.json");
        try
        {
            using var process = Process.GetCurrentProcess();
            var executablePath = Environment.ProcessPath ?? process.MainModule?.FileName
                ?? throw new InvalidOperationException("Test process path is unavailable.");
            var registry = new McpProcessRegistry(registryPath);
            const int exitedProcessId = 999_999;
            registry.Register(new McpProcessRegistration(exitedProcessId, executablePath, DateTimeOffset.Now));
            registry.Register(new McpProcessRegistration(process.Id, executablePath, process.StartTime));

            Assert.Contains($"\"processId\": {process.Id}", File.ReadAllText(registryPath));
            Assert.DoesNotContain($"\"processId\": {exitedProcessId}", File.ReadAllText(registryPath));
            Assert.Empty(registry.GetLiveRegistrations(executablePath));
            registry.Unregister(process.Id);

            Assert.DoesNotContain($"\"processId\": {process.Id}", File.ReadAllText(registryPath));
            Assert.False(File.Exists(registryPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UiTextUsesSelectedDisplayLanguageWithoutChangingStoredContent()
    {
        Assert.Equal("Settings", UiText.Get("设置", AppLanguage.English));
        Assert.Equal("设置", UiText.Get("设置", AppLanguage.Chinese));
        Assert.Equal("Project name", UiText.Get("Project name", AppLanguage.English));
        Assert.Equal("Theme", UiText.Get("主题", AppLanguage.English));
        Assert.Equal("Open", UiText.Get("打开", AppLanguage.English));
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
    public void FreshAssistStartsAtNewChatInsteadOfBoundConversation()
    {
        Assert.Equal(
            "https://m365.cloud.microsoft/chat/",
            Mcp.CopilotBridgeTools.ResolvePrimaryConversationUrl(
                CollaborationMode.Assist,
                null,
                "https://m365.cloud.microsoft/chat/conversation/bound",
                true,
                "m365.cloud.microsoft"));
    }

    [Fact]
    public void AssistFollowUpReusesStoredConversation()
    {
        Assert.Equal(
            "https://m365.cloud.microsoft/chat/conversation/existing",
            Mcp.CopilotBridgeTools.ResolvePrimaryConversationUrl(
                CollaborationMode.Assist,
                "https://m365.cloud.microsoft/chat/conversation/existing",
                "https://m365.cloud.microsoft/chat/conversation/bound",
                false,
                "m365.cloud.microsoft"));
    }

    [Fact]
    public void ReviewDoesNotUsePrimaryConversation()
    {
        Assert.Null(Mcp.CopilotBridgeTools.ResolvePrimaryConversationUrl(
            CollaborationMode.Review,
            null,
            "https://m365.cloud.microsoft/chat/conversation/bound",
            true,
            "m365.cloud.microsoft"));
    }

    [Theory]
    [InlineData("Microsoft 365 Copilot login is required.", "login_required")]
    [InlineData("Daily Edge has no DevToolsActivePort.", "remote_debugging_disabled")]
    [InlineData("WebSocket error: connect ECONNREFUSED; ws connecting", "remote_debugging_disabled")]
    [InlineData("No eligible Microsoft 365 Copilot chat tab was found.", "tab_rebind_required")]
    [InlineData("Target page, context or browser has been closed", "tab_rebind_required")]
    [InlineData("No allowed model is available.", "no_eligible_model")]
    public void PreSubmitFailuresHaveStableG7ErrorCodes(string message, string expected)
    {
        Assert.Equal(expected, Mcp.CopilotBridgeTools.MapPreSubmitError(
            new InvalidOperationException(message)));
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
    public async Task ConsultationStatePersistsModeBudgetsAndReviewerUrlsWithoutBodies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "consultations.json");
        var store = new ConsultationStateStore(path);
        try
        {
            var expected = new ConsultationRecord
            {
                Mode = "review",
                TurnCount = 1,
                ComplexityConversationUrl = "https://m365.cloud.microsoft/chat/complexity",
                EvidenceConversationUrl = "https://m365.cloud.microsoft/chat/evidence",
                Status = "completed",
                LastModel = "opus"
            };
            await store.SaveAsync("review-a", expected);

            var actual = await store.FindAsync("review-a");
            Assert.NotNull(actual);
            Assert.Equal("review", actual.Mode);
            Assert.Equal(1, actual.TurnCount);
            Assert.NotEqual(actual.ComplexityConversationUrl, actual.EvidenceConversationUrl);
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reply", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("markdown", json, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(path + ".tmp"));
            using var document = JsonDocument.Parse(json);
            var keys = document.RootElement.GetProperty("conversations")
                .GetProperty("review-a")
                .EnumerateObject()
                .Select(item => item.Name)
                .Order()
                .ToArray();
            Assert.Equal(
                new[]
                {
                    "complexityConversationUrl", "evidenceConversationUrl", "lastModel", "mode",
                    "primaryConversationUrl", "status", "turnCount", "updatedAt"
                }.Order(),
                keys);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConversationWorkspaceStoresImmediateMarkdownAndKeepsStableIdentityAcrossRenameAndMove()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var store = new ConversationWorkspaceStore(root);
        try
        {
            var project = await store.CreateProjectAsync("项目一");
            var conversation = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId);
            conversation = conversation with
            {
                CopilotConversationUrl = "https://m365.cloud.microsoft/chat/conversation/conversation-1",
                CopilotConversationId = "conversation-1",
                CopilotTitleInitial = "网页原标题",
                CopilotTitleCurrent = "网页当前标题"
            };
            await store.SaveAsync(conversation);

            var result = new CollaborationRunResult(
                [new ReviewerResult(
                    "primary",
                    "# 任务\n\n请检查方案。",
                    new AssistResult(
                        "Opus",
                        "# 结论\n\n方案可行。",
                        conversation.CopilotConversationUrl,
                        1,
                        1,
                        false))],
                1,
                conversation.CopilotConversationUrl,
                null,
                null);
            conversation = await store.AppendRunAsync(conversation, result);

            Assert.Equal("conversation-1", conversation.CopilotConversationId);
            Assert.Equal(2, conversation.Turns.Count);
            Assert.Single(store.Search(conversation, "方案可行"));
            var beforeMove = Path.Combine(root, ConversationWorkspaceStore.StandaloneProjectId, $"conversation-{conversation.Id}.md");
            var markdown = await File.ReadAllTextAsync(beforeMove);
            Assert.Contains("# 任务", markdown);
            Assert.Contains("# 结论", markdown);
            Assert.Contains("实际模型：`Opus`", markdown);

            conversation = await store.RenameAsync(conversation, "本地项目标题");
            conversation = await store.MoveAsync(conversation, project.Id);
            var restored = await store.FindAsync(conversation.Id);

            Assert.NotNull(restored);
            Assert.Equal("本地项目标题", restored.DisplayTitle);
            Assert.Equal("网页原标题", restored.CopilotTitleInitial);
            Assert.Equal("网页当前标题", restored.CopilotTitleCurrent);
            Assert.Equal(project.Id, restored.ProjectId);
            Assert.False(File.Exists(beforeMove));
            Assert.True(File.Exists(Path.Combine(root, project.Id, $"conversation-{conversation.Id}.md")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConversationWorkspaceDisplayMarkdownHidesInternalMetadata()
    {
        var store = new ConversationWorkspaceStore(Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N")));
        var conversation = new ConversationDocument
        {
            CopilotTitleInitial = "网页原标题",
            CopilotTitleCurrent = "网页当前标题",
            Turns = [new ConversationTurn(DateTimeOffset.Now, "user", "正文内容")]
        };

        var stored = store.Render(conversation);
        var displayed = store.RenderForDisplay(conversation);

        Assert.StartsWith("<!-- copilot-bridge-conversation:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("copilot-bridge-conversation:", displayed, StringComparison.Ordinal);
        Assert.StartsWith("---", displayed, StringComparison.Ordinal);
        Assert.Contains("# 网页当前标题", displayed, StringComparison.Ordinal);
        Assert.Contains("正文内容", displayed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoricalImportKeepsCopilotTitleAndUnknownModelsWithoutDuplicateUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var store = new ConversationWorkspaceStore(root);
        var snapshot = new HistoricalConversationSnapshot(
            "https://m365.cloud.microsoft/chat/conversation/imported-1",
            "网页旧对话标题",
            "Opus",
            [
                new HistoricalConversationTurn("user", "旧问题"),
                new HistoricalConversationTurn("copilot", "旧回复")
            ]);
        try
        {
            var imported = await store.ImportHistoricalConversationAsync(snapshot);

            Assert.Equal(ConversationWorkspaceStore.StandaloneProjectId, imported.ProjectId);
            Assert.Equal("网页旧对话标题", imported.CopilotTitleInitial);
            Assert.Equal("网页旧对话标题", imported.DisplayTitle);
            Assert.Equal(["user", "copilot"], imported.Turns.Select(turn => turn.Role));
            Assert.Equal("unknown", imported.Turns[1].ModelStatus);
            Assert.Null(imported.Turns[1].Model);
            Assert.Contains("模型状态：`unknown`", await File.ReadAllTextAsync(
                Path.Combine(root, ConversationWorkspaceStore.StandaloneProjectId, $"conversation-{imported.Id}.md")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportHistoricalConversationAsync(snapshot));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(true, true, false, 0, 10)]
    [InlineData(false, true, false, 0, 60)]
    [InlineData(true, false, false, 0, 60)]
    [InlineData(true, true, true, 0, 60)]
    [InlineData(true, true, false, 1, 30)]
    [InlineData(true, true, false, 2, 60)]
    [InlineData(true, true, false, 3, 120)]
    [InlineData(true, true, false, 8, 120)]
    public void StatusRefreshScheduleUsesAdaptiveIntervals(
        bool overviewIsVisible,
        bool windowIsActive,
        bool windowIsMinimized,
        int failures,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            StatusRefreshSchedule.NextInterval(
                overviewIsVisible,
                windowIsActive,
                windowIsMinimized,
                failures));
    }

    [Fact]
    public void DefaultConsultationStateIsPerUserLocalData()
    {
        var store = new ConsultationStateStore();
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localData, store.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("CopilotBridge", "consultations.json"),
            store.FilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData((int)CollaborationMode.Assist)]
    [InlineData((int)CollaborationMode.Outsource)]
    public async Task CollaborationModesContinueWithoutATurnLimit(int mode)
    {
        var calls = 0;
        var runner = new CollaborationRunner(request =>
        {
            calls++;
            return Task.FromResult(new AssistResult(
                "Opus",
                "reply",
                request.ConversationUrl ?? "https://m365.cloud.microsoft/chat/",
                1,
                1,
                false));
        });
        var context = new CollaborationContext(
            "request",
            (CollaborationMode)mode,
            10_000,
            "https://m365.cloud.microsoft/chat/primary",
            null,
            null);

        var result = await runner.RunAsync(context);

        Assert.Equal(1, calls);
        Assert.Equal(10_001, result.TurnCount);
    }

    [Fact]
    public async Task ReviewRunsTwoIsolatedRolesSerially()
    {
        var requests = new List<AssistRequest>();
        var active = 0;
        var maximumActive = 0;
        var runner = new CollaborationRunner(async request =>
        {
            requests.Add(request);
            active++;
            maximumActive = Math.Max(maximumActive, active);
            await Task.Delay(10);
            active--;
            var reviewer = requests.Count == 1 ? "complexity" : "evidence";
            return new AssistResult(
                "Opus",
                reviewer,
                $"https://m365.cloud.microsoft/chat/{reviewer}",
                1,
                1,
                false);
        });

        var result = await runner.RunAsync(new CollaborationContext(
            "shared review request",
            CollaborationMode.Review,
            0,
            null,
            null,
            null));

        Assert.Equal(1, maximumActive);
        Assert.Equal(["complexity", "evidence"], result.Responses.Select(item => item.Reviewer));
        Assert.Equal(2, requests.Count);
        Assert.Contains("Complexity and boundaries", requests[0].Prompt);
        Assert.DoesNotContain("Failure modes and evidence", requests[0].Prompt);
        Assert.Contains("Failure modes and evidence", requests[1].Prompt);
        Assert.DoesNotContain("Complexity and boundaries", requests[1].Prompt);
        Assert.All(requests, request => Assert.Contains("shared review request", request.Prompt));
        Assert.NotEqual(result.ComplexityConversationUrl, result.EvidenceConversationUrl);
    }

    [Fact]
    public async Task ReviewDoesNotRetryComplexityWhenEvidenceFails()
    {
        var calls = 0;
        var runner = new CollaborationRunner(request =>
        {
            calls++;
            if (calls == 2) throw new InvalidOperationException("evidence pre-submit failure");
            return Task.FromResult(new AssistResult(
                "Opus",
                "complexity",
                "https://m365.cloud.microsoft/chat/complexity",
                1,
                1,
                false));
        });

        var exception = await Assert.ThrowsAsync<PartialReviewException>(() => runner.RunAsync(
            new CollaborationContext("request", CollaborationMode.Review, 0, null, null, null)));

        Assert.Equal(2, calls);
        Assert.Equal("complexity", exception.Completed.Reviewer);
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
