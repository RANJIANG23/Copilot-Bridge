using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Playwright;

namespace CopilotBridge.Probe;

internal sealed record ProbeOptions(
    string? Endpoint,
    bool VerifyBackgroundTarget,
    bool SelectModel,
    bool VerifyTestTurn,
    bool SendTest)
{
    public static ProbeOptions Parse(string[] args)
    {
        string? endpoint = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].Equals("--endpoint", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("--endpoint requires an http or websocket URL.");
            }

            endpoint = args[++index];
        }

        return new ProbeOptions(
            endpoint,
            args.Contains("--verify-background-target", StringComparer.OrdinalIgnoreCase),
            args.Contains("--select-model", StringComparer.OrdinalIgnoreCase),
            args.Contains("--verify-test-turn", StringComparer.OrdinalIgnoreCase),
            args.Contains("--send-test", StringComparer.OrdinalIgnoreCase));
    }
}

internal static class EdgeProbe
{
    private const string TestPrompt = "这是 Copilot Bridge 的连接测试。请只回复：COPILOT_BRIDGE_TEST_OK";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(ProbeOptions options)
    {
        var endpoint = options.Endpoint ?? ReadDailyEdgeEndpoint();
        if (endpoint is null)
        {
            return WriteFailure(
                "edge_remote_debugging_unavailable",
                "Daily Edge has no DevToolsActivePort. Enable remote debugging for this Edge instance, then retry.");
        }

        var foregroundBefore = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out var cursorBefore);
        var submitted = false;

        try
        {
            using var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.ConnectOverCDPAsync(
                endpoint,
                new BrowserTypeConnectOverCDPOptions { Timeout = 10_000 });

            var page = await FindSingleCopilotPageAsync(browser);
            var context = page.Context;
            var pageTitle = await page.TitleAsync();
            string? backgroundEvidence = null;

            if (options.VerifyBackgroundTarget)
            {
                backgroundEvidence = await VerifyBackgroundTargetAsync(browser, context);
            }

            string? selectedModel = null;
            string? reply = null;
            object? testTurnEvidence = null;

            if (options.SelectModel || options.SendTest)
            {
                selectedModel = await SelectAllowedModelAsync(page);
            }

            if (options.SendTest)
            {
                var sendResult = await SendOnceAndReadReplyAsync(page, TestPrompt);
                submitted = sendResult.Submitted;
                reply = sendResult.Reply;
            }

            if (options.VerifyTestTurn)
            {
                testTurnEvidence = await VerifySingleTestTurnAsync(page);
            }

            NativeMethods.GetCursorPos(out var cursorAfter);
            var foregroundAfter = NativeMethods.GetForegroundWindow();
            var foregroundPreserved = foregroundBefore == foregroundAfter;
            var cursorPreserved = cursorBefore.Equals(cursorAfter);
            var externalInteractionObserved = !cursorPreserved;
            var possibleForegroundSteal = !foregroundPreserved && cursorPreserved;

            WriteJson(new
            {
                status = "ok",
                endpoint,
                page = new { url = page.Url, title = pageTitle },
                backgroundTarget = backgroundEvidence,
                selectedModel,
                submitted,
                reply,
                testTurnEvidence,
                foregroundPreserved,
                cursorPreserved,
                externalInteractionObserved,
                possibleForegroundSteal,
                observations = new
                {
                    foregroundBefore = foregroundBefore.ToInt64(),
                    foregroundAfter = foregroundAfter.ToInt64(),
                    cursorBefore = new { x = cursorBefore.X, y = cursorBefore.Y },
                    cursorAfter = new { x = cursorAfter.X, y = cursorAfter.Y }
                }
            });

            return possibleForegroundSteal ? 1 : 0;
        }
        catch (SubmissionUnknownException exception)
        {
            WriteJson(new
            {
                status = "submission_unknown",
                canRetrySafely = false,
                message = exception.Message
            });
            return 3;
        }
        catch (Exception exception)
        {
            WriteJson(new
            {
                status = submitted ? "submission_unknown" : "not_submitted",
                canRetrySafely = !submitted,
                error = exception.GetType().Name,
                message = exception.Message
            });
            return submitted ? 3 : 1;
        }
    }

    private static string? ReadDailyEdgeEndpoint()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var portFile = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "DevToolsActivePort");
        if (!File.Exists(portFile))
        {
            return null;
        }

        var lines = File.ReadAllLines(portFile);
        var port = lines.FirstOrDefault()?.Trim();
        if (!int.TryParse(port, out var parsedPort) || parsedPort is <= 0 or > 65535)
        {
            return null;
        }

        var browserPath = lines.ElementAtOrDefault(1)?.Trim();
        return browserPath is not null &&
               browserPath.StartsWith("/devtools/browser/", StringComparison.Ordinal) &&
               Uri.TryCreate($"ws://127.0.0.1:{parsedPort}{browserPath}", UriKind.Absolute, out var websocketEndpoint)
            ? websocketEndpoint.AbsoluteUri
            : $"http://127.0.0.1:{parsedPort}";
    }

    private static async Task<IPage> FindSingleCopilotPageAsync(IBrowser browser)
    {
        var matches = browser.Contexts
            .SelectMany(context => context.Pages)
            .Where(page => IsAllowedCopilotUrl(page.Url))
            .ToArray();

        return matches.Length switch
        {
            1 => await Task.FromResult(matches[0]),
            0 => throw new InvalidOperationException(
                "No eligible Microsoft 365 Copilot chat tab was found in the connected Edge instance."),
            _ => throw new InvalidOperationException(
                $"Found {matches.Length} eligible Copilot tabs. Keep exactly one dedicated chat tab open for Phase 1.")
        };
    }

    private static bool IsAllowedCopilotUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("m365.cloud.microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.Equals("/chat", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.StartsWith("/chat/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> VerifyBackgroundTargetAsync(IBrowser browser, IBrowserContext context)
    {
        var existingPages = context.Pages.ToArray();
        var visibilityBefore = await ReadVisibilityAsync(existingPages);
        var session = await browser.NewBrowserCDPSessionAsync();
        string? targetId = null;

        try
        {
            var createArguments = new Dictionary<string, object>
            {
                ["url"] = "about:blank",
                ["background"] = true
            };
            var contextId = await ReadBrowserContextIdAsync(context);
            if (!string.IsNullOrWhiteSpace(contextId))
            {
                createArguments["browserContextId"] = contextId;
            }

            var result = await session.SendAsync(
                "Target.createTarget",
                createArguments);

            targetId = result is JsonElement payload
                ? payload.GetProperty("targetId").GetString()
                : null;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidOperationException("CDP did not return a targetId for the background page.");
            }

            await Task.Delay(500);
            var visibilityAfter = await ReadVisibilityAsync(existingPages);
            if (!visibilityBefore.SequenceEqual(visibilityAfter))
            {
                throw new InvalidOperationException("Creating a background target changed the visibility of an existing page.");
            }

            return "Target.createTarget(background=true) preserved all existing page visibility states.";
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                await session.SendAsync(
                    "Target.closeTarget",
                    new Dictionary<string, object> { ["targetId"] = targetId });
            }

            await session.DetachAsync();
        }
    }

    private static async Task<string?> ReadBrowserContextIdAsync(IBrowserContext context)
    {
        var page = context.Pages.FirstOrDefault() ??
                   throw new InvalidOperationException("The target browser context contains no pages.");
        var session = await context.NewCDPSessionAsync(page);

        try
        {
            var result = await session.SendAsync("Target.getTargetInfo");
            if (result is not JsonElement payload ||
                !payload.TryGetProperty("targetInfo", out var targetInfo) ||
                !targetInfo.TryGetProperty("browserContextId", out var browserContextId))
            {
                return null;
            }

            return browserContextId.GetString();
        }
        finally
        {
            await session.DetachAsync();
        }
    }

    private static async Task<string[]> ReadVisibilityAsync(IEnumerable<IPage> pages)
    {
        var states = new List<string>();
        foreach (var page in pages)
        {
            states.Add($"{page.Url}|{await page.EvaluateAsync<string>("document.visibilityState")}");
        }

        return states.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static async Task<string> SelectAllowedModelAsync(IPage page)
    {
        var switcher = await FindUniqueVisibleAsync(
            page,
            "model switcher",
            "button#gptModeSwitcher",
            "[data-testid='gpt-mode-switcher']");

        await switcher.ClickAsync();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        await Task.Delay(2_000);

        while (DateTime.UtcNow < deadline)
        {
            if (await IsExactTextVisibleAsync(page, "Opus"))
            {
                break;
            }

            await Task.Delay(250);
        }

        string selected;
        if (await TryClickExactAsync(page, "Opus"))
        {
            selected = "Opus";
        }
        else if (await TrySelectGpt56Async(page))
        {
            selected = "GPT 5.6 Think deeper";
        }
        else if (await TryClickExactAsync(page, "深度思考") || await TryClickExactAsync(page, "Think deeper"))
        {
            selected = "深度思考";
        }
        else
        {
            throw new InvalidOperationException(
                "No allowed model was available after menu hydration. Fast and automatic modes are intentionally forbidden.");
        }

        await Task.Delay(300);
        var readback = Normalize(await switcher.InnerTextAsync());
        if (!ModelReadbackMatches(selected, readback))
        {
            throw new InvalidOperationException($"Model selection readback failed. Expected '{selected}', switcher shows '{readback}'.");
        }

        return selected;
    }

    private static async Task<object> VerifySingleTestTurnAsync(IPage page)
    {
        var userMessages = page.Locator("[data-testid='chatQuestion'], [data-message-author-role='user']");
        var assistantMessages = page.Locator(
            "[data-testid='copilot-message-reply-div'] [data-testid='markdown-reply'], " +
            "[data-message-author-role='assistant']");
        var promptMatches = userMessages.Filter(new LocatorFilterOptions { HasText = TestPrompt });
        var replyMatches = assistantMessages.Filter(new LocatorFilterOptions { HasText = "COPILOT_BRIDGE_TEST_OK" });
        var promptCount = await promptMatches.CountAsync();
        var replyCount = await replyMatches.CountAsync();

        if (promptCount != 1 || replyCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one test prompt and one test reply; found prompts={promptCount}, replies={replyCount}.");
        }

        return new { promptCount, replyCount };
    }

    private static async Task<bool> TrySelectGpt56Async(IPage page)
    {
        var direct = page.GetByText("GPT 5.6 Think deeper", new PageGetByTextOptions { Exact = true });
        if (await TryClickSingleEnabledAsync(direct))
        {
            return true;
        }

        var parent = page.GetByText("GPT", new PageGetByTextOptions { Exact = true });
        var visibleParent = await SingleVisibleAsync(parent);
        if (visibleParent is null || !await visibleParent.IsEnabledAsync())
        {
            return false;
        }

        await visibleParent.HoverAsync();
        try
        {
            await direct.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 2_000
            });
        }
        catch (TimeoutException)
        {
            await visibleParent.ClickAsync();
        }

        return await TryClickSingleEnabledAsync(direct);
    }

    private static bool ModelReadbackMatches(string selected, string readback) => selected switch
    {
        "Opus" => readback.Contains("Opus", StringComparison.OrdinalIgnoreCase),
        "GPT 5.6 Think deeper" => readback.Contains("GPT 5.6", StringComparison.OrdinalIgnoreCase),
        _ => readback.Contains("深度思考", StringComparison.OrdinalIgnoreCase) ||
             readback.Contains("Think deeper", StringComparison.OrdinalIgnoreCase)
    };

    private static async Task<SendResult> SendOnceAndReadReplyAsync(IPage page, string prompt)
    {
        await EnsureIdleAsync(page);
        var composer = await FindUniqueVisibleAsync(
            page,
            "message composer",
            "textarea[placeholder*='消息']",
            "textarea[placeholder*='输入']",
            "textarea[placeholder*='Ask']",
            "textarea[placeholder*='Message']",
            "[contenteditable='true'][role='textbox']");

        await composer.FillAsync(prompt);
        var readback = await ReadComposerTextAsync(composer);
        var canonicalReadback = readback
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .TrimEnd('\n', '\u200B', '\u200C', '\u200D', '\uFEFF');
        if (!canonicalReadback.Equals(prompt, StringComparison.Ordinal))
        {
            await composer.FillAsync(string.Empty);
            throw new InvalidOperationException(
                $"Composer readback does not exactly match the test prompt. " +
                $"ExpectedLength={prompt.Length}, Actual={JsonSerializer.Serialize(readback)}.");
        }

        await EnsureIdleAsync(page);
        var userMessages = page.Locator("[data-testid='chatQuestion'], [data-message-author-role='user']");
        var assistantMessages = page.Locator(
            "[data-testid='copilot-message-reply-div'] [data-testid='markdown-reply'], " +
            "[data-message-author-role='assistant']");
        var userCountBefore = await userMessages.CountAsync();
        var assistantCountBefore = await assistantMessages.CountAsync();
        var send = await FindUniqueVisibleAsync(
            page,
            "send button",
            "button[aria-label*='发送']",
            "button[aria-label*='Send']",
            "button[data-testid*='send']");

        if (!await send.IsEnabledAsync())
        {
            throw new InvalidOperationException("The verified send button is disabled.");
        }

        try
        {
            await send.ClickAsync();
            await WaitUntilAsync(
                async () => await userMessages.CountAsync() > userCountBefore ||
                            await IsExactTextVisibleAsync(page, prompt),
                TimeSpan.FromSeconds(15),
                "The page did not expose a new user message after the single send click.");

            await WaitUntilAsync(
                async () => await assistantMessages.CountAsync() > assistantCountBefore,
                TimeSpan.FromMinutes(5),
                "Copilot did not expose a new assistant reply before the timeout.");

            var lastReply = assistantMessages.Last;
            string? previous = null;
            var stableReadings = 0;
            var deadline = DateTime.UtcNow.AddMinutes(5);

            while (DateTime.UtcNow < deadline)
            {
                var current = Normalize(await lastReply.InnerTextAsync());
                var idle = await IsIdleAsync(page);

                if (idle && current.Length > 0 && current.Equals(previous, StringComparison.Ordinal))
                {
                    stableReadings++;
                    if (stableReadings >= 2)
                    {
                        return new SendResult(true, current);
                    }
                }
                else
                {
                    stableReadings = 0;
                }

                previous = current;
                await Task.Delay(750);
            }

            throw new TimeoutException("A reply appeared, but its completed state could not be verified.");
        }
        catch (SubmissionUnknownException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SubmissionUnknownException(
                $"The single send click was attempted, but completion could not be verified: {exception.Message}",
                exception);
        }
    }

    private static async Task EnsureIdleAsync(IPage page)
    {
        if (!await IsIdleAsync(page))
        {
            throw new InvalidOperationException("Copilot is currently generating or the chat surface is busy.");
        }
    }

    private static Task<string> ReadComposerTextAsync(ILocator composer) =>
        composer.EvaluateAsync<string>(
            "element => element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement " +
            "? element.value : (element.innerText ?? '')");

    private static async Task<bool> IsIdleAsync(IPage page)
    {
        var busy = page.Locator(
            "[aria-busy='true'], button[aria-label*='停止'], button[aria-label*='Stop'], " +
            "button[data-testid*='stop']");

        for (var index = 0; index < await busy.CountAsync(); index++)
        {
            if (await busy.Nth(index).IsVisibleAsync())
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<ILocator> FindUniqueVisibleAsync(
        IPage page,
        string description,
        params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var match = await SingleVisibleAsync(page.Locator(selector));
            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"Could not identify exactly one visible {description}.");
    }

    private static async Task<bool> IsExactTextVisibleAsync(IPage page, string text) =>
        await SingleVisibleAsync(page.GetByText(text, new PageGetByTextOptions { Exact = true })) is not null;

    private static async Task<bool> TryClickExactAsync(IPage page, string text) =>
        await TryClickSingleEnabledAsync(page.GetByText(text, new PageGetByTextOptions { Exact = true }));

    private static async Task<bool> TryClickSingleEnabledAsync(ILocator locator)
    {
        var match = await SingleVisibleAsync(locator);
        if (match is null || !await match.IsEnabledAsync())
        {
            return false;
        }

        await match.ClickAsync();
        return true;
    }

    private static async Task<ILocator?> SingleVisibleAsync(ILocator locator)
    {
        ILocator? match = null;
        for (var index = 0; index < await locator.CountAsync(); index++)
        {
            var candidate = locator.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int WriteFailure(string status, string message)
    {
        WriteJson(new { status, canRetrySafely = true, message });
        return 1;
    }

    private static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private sealed record SendResult(bool Submitted, string Reply);
}

internal sealed class SubmissionUnknownException : Exception
{
    public SubmissionUnknownException(string message)
        : base(message)
    {
    }

    public SubmissionUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point : IEquatable<Point>
{
    internal int X;
    internal int Y;

    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;
}
