using CopilotBridge.Core;
using Microsoft.Playwright;

namespace CopilotBridge.Browser;

internal sealed record PageTurnResult(
    string ReplyMarkdown,
    string ConversationUrl,
    int UserMessageDelta,
    int AssistantMessageDelta);

internal sealed record HistoricalConversationTurn(string Role, string Markdown);

internal sealed record HistoricalConversationSnapshot(
    string ConversationUrl,
    string CopilotTitle,
    string? CurrentPageModel,
    IReadOnlyList<HistoricalConversationTurn> Turns);

internal sealed class SubmissionUnknownException : Exception
{
    internal SubmissionUnknownException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class ReplyTimeoutException : Exception
{
    internal ReplyTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class PageOverlayBlockedException : InvalidOperationException
{
    internal PageOverlayBlockedException(string message)
        : base(message)
    {
    }
}

internal sealed class ModelSelectorBlockedException : InvalidOperationException
{
    internal ModelSelectorBlockedException(string message)
        : base(message)
    {
    }
}

internal sealed class CopilotPageDriver
{
    private static readonly TimeSpan SubmissionAcknowledgementTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SurfaceReadyTimeout = TimeSpan.FromSeconds(15);
    private readonly IPage _page;
    private readonly ProviderSelectors _selectors;
    private readonly BridgeSettings _settings;

    internal CopilotPageDriver(IPage page, ProviderSelectors selectors, BridgeSettings settings)
    {
        _page = page;
        _selectors = selectors;
        _settings = settings;
    }

    internal async Task<string> ReadCurrentModelAsync()
    {
        await EnsureAuthenticatedAsync();
        var switcher = await WaitForUniqueVisibleAsync("model switcher", _selectors.ModelSwitcher);
        return Normalize(await switcher.InnerTextAsync());
    }

    internal async Task<string> SelectAllowedModelAsync()
    {
        await EnsureAuthenticatedAsync();
        var switcher = await WaitForUniqueVisibleAsync("model switcher", _selectors.ModelSwitcher);
        var priority = ModelPriorityOptions.Parse(_settings.ModelPriority);
        var menuAlreadyOpen = await IsAnyAllowedModelOptionVisibleAsync();
        var readbackBefore = Normalize(await switcher.InnerTextAsync());
        if (!menuAlreadyOpen && ModelReadbackMatches(priority[0], readbackBefore))
        {
            return priority[0];
        }

        if (!menuAlreadyOpen)
        {
            await OpenModelMenuAsync(switcher);
        }

        var started = DateTime.UtcNow;
        var deadline = started.AddMilliseconds(_settings.MenuMaximumWaitMilliseconds);
        var minimum = started.AddMilliseconds(_settings.MenuMinimumWaitMilliseconds);

        while (DateTime.UtcNow < minimum)
        {
            await Task.Delay(Math.Min(50, Math.Max(1, _settings.MenuMinimumWaitMilliseconds)));
        }

        while (DateTime.UtcNow < deadline)
        {
            if (await IsExactTextVisibleAsync(_selectors.Opus))
            {
                break;
            }

            await Task.Delay(50);
        }

        string? selected = null;
        foreach (var preferredModel in priority)
        {
            if (preferredModel.Equals(_selectors.Opus, StringComparison.Ordinal) &&
                await TryClickModelOptionAsync(_selectors.Opus))
            {
                selected = _selectors.Opus;
                break;
            }

            if (preferredModel.Equals(_selectors.Gpt56, StringComparison.Ordinal) &&
                await TrySelectGpt56Async())
            {
                selected = _selectors.Gpt56;
                break;
            }

            if (preferredModel.Equals(_selectors.DeepThinkingChinese, StringComparison.Ordinal) &&
                (await TryClickModelOptionAsync(_selectors.DeepThinkingChinese) ||
                 await TryClickModelOptionAsync(_selectors.DeepThinkingEnglish)))
            {
                selected = _selectors.DeepThinkingChinese;
                break;
            }
        }

        if (selected is null)
        {
            throw new InvalidOperationException(
                "No allowed model is available. Automatic and fast modes are intentionally forbidden.");
        }

        await Task.Delay(100);
        var readback = Normalize(await switcher.InnerTextAsync());
        if (!ModelReadbackMatches(selected, readback))
        {
            throw new InvalidOperationException(
                $"Model readback failed. Expected '{selected}', switcher shows '{readback}'.");
        }

        return selected;
    }

    internal async Task<PageTurnResult> SendAndReadAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        await EnsureAuthenticatedAsync();
        await EnsureIdleAsync();
        var composer = await FindUniqueVisibleAsync("message composer", _selectors.Composer);
        await composer.FillAsync(prompt);
        var readback = CanonicalComposerText(await ReadComposerTextAsync(composer));
        if (!readback.Equals(prompt, StringComparison.Ordinal))
        {
            await composer.FillAsync(string.Empty);
            throw new InvalidOperationException("Composer readback does not exactly match the prompt.");
        }

        await EnsureIdleAsync();
        var users = _page.Locator(_selectors.UserMessages);
        var assistants = _page.Locator(_selectors.AssistantMessages);
        var userCountBefore = await users.CountAsync();
        var assistantCountBefore = await assistants.CountAsync();
        var lastUserBefore = await ReadLastMessageFingerprintAsync(users);
        var lastAssistantBefore = await ReadLastMessageFingerprintAsync(assistants);
        var send = await FindUniqueVisibleAsync("send button", _selectors.SendButton);
        if (!await send.IsEnabledAsync())
        {
            await composer.FillAsync(string.Empty);
            throw new InvalidOperationException("The verified send button is disabled.");
        }

        var userMessageVerified = false;
        try
        {
            await send.ClickAsync();
            await WaitUntilAsync(
                async () => await MessageAdvancedAsync(users, userCountBefore, lastUserBefore),
                SubmissionAcknowledgementTimeout,
                "The page did not expose a new user message after the single send click.");
            userMessageVerified = true;
            await WaitUntilAsync(
                async () =>
                {
                    await ThrowIfPageErrorAsync();
                    return await MessageAdvancedAsync(
                        assistants,
                        assistantCountBefore,
                        lastAssistantBefore);
                },
                TimeSpan.FromSeconds(_settings.ReplyTimeoutSeconds),
                "Copilot did not expose a new assistant reply before the timeout.");

            var lastReply = assistants.Last;
            await WaitForStableReplyAsync(lastReply);
            var markdown = await RenderedMarkdownExtractor.ExtractAsync(lastReply);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                throw new InvalidOperationException("The completed assistant reply is empty.");
            }

            return new PageTurnResult(
                markdown,
                _page.Url,
                await ObservedMessageDeltaAsync(users, userCountBefore, lastUserBefore),
                await ObservedMessageDeltaAsync(
                    assistants,
                    assistantCountBefore,
                    lastAssistantBefore));
        }
        catch (SubmissionUnknownException)
        {
            throw;
        }
        catch (TimeoutException exception) when (userMessageVerified)
        {
            throw new ReplyTimeoutException(
                $"The message was submitted, but the reply did not complete before timeout: {exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            throw new SubmissionUnknownException(
                $"The send click was attempted, but completion could not be verified: {exception.Message}",
                exception);
        }
    }

    internal async Task<(int PromptCount, int ReplyCount)> CountExactTurnAsync(
        string prompt,
        string reply)
    {
        var promptCount = await _page.Locator(_selectors.UserMessages)
            .Filter(new LocatorFilterOptions { HasText = prompt })
            .CountAsync();
        var replyCount = await _page.Locator(_selectors.AssistantMessages)
            .Filter(new LocatorFilterOptions { HasText = reply })
            .CountAsync();
        return (promptCount, replyCount);
    }

    // This is intentionally read-only. It captures only the turns currently exposed by
    // the Copilot DOM; it does not scroll, navigate, send, or infer historic model usage.
    internal async Task<HistoricalConversationSnapshot> ReadCurrentConversationAsync()
    {
        await EnsureAuthenticatedAsync();
        await EnsureIdleAsync();

        if (!_selectors.IsAllowedConversationUrl(_page.Url))
        {
            throw new InvalidOperationException("Open an existing Copilot conversation before importing it.");
        }

        var messages = _page.Locator($"{_selectors.UserMessages}, {_selectors.AssistantMessages}");
        var turns = new List<HistoricalConversationTurn>();
        var userSelector = System.Text.Json.JsonSerializer.Serialize(_selectors.UserMessages);
        for (var index = 0; index < await messages.CountAsync(); index++)
        {
            var message = messages.Nth(index);
            if (!await message.IsVisibleAsync()) continue;
            var markdown = await RenderedMarkdownExtractor.ExtractAsync(message);
            if (string.IsNullOrWhiteSpace(markdown)) continue;
            var isUser = await message.EvaluateAsync<bool>($"element => element.matches({userSelector})");
            turns.Add(new HistoricalConversationTurn(isUser ? "user" : "copilot", markdown));
        }

        if (turns.Count == 0)
        {
            throw new InvalidOperationException("The current Copilot conversation has no loaded messages to import.");
        }

        var title = Normalize(await _page.TitleAsync());
        if (string.IsNullOrWhiteSpace(title) || title.Equals("Microsoft 365 Copilot", StringComparison.OrdinalIgnoreCase))
        {
            title = turns.FirstOrDefault(turn => turn.Role == "user")?.Markdown
                .Replace("\n", " ", StringComparison.Ordinal).Trim() ?? "未命名 Copilot 对话";
            title = title.Length > 80 ? title[..80] : title;
        }

        string? currentModel;
        try { currentModel = await ReadCurrentModelAsync(); }
        catch { currentModel = null; }
        return new HistoricalConversationSnapshot(_page.Url, title, currentModel, turns);
    }

    private async Task WaitForStableReplyAsync(ILocator reply)
    {
        string? previous = null;
        var stableReadings = 0;
        var deadline = DateTime.UtcNow.AddSeconds(_settings.ReplyTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            await ThrowIfPageErrorAsync();
            var current = Normalize(await reply.InnerTextAsync());
            if (await IsIdleAsync() && current.Length > 0 && current.Equals(previous, StringComparison.Ordinal))
            {
                stableReadings++;
                if (stableReadings >= 2)
                {
                    return;
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

    private async Task<bool> TrySelectGpt56Async()
    {
        if (await TryClickModelOptionAsync(_selectors.Gpt56))
        {
            return true;
        }

        var parent = await SingleVisibleModelOptionAsync(_selectors.GptParent);
        if (parent is null || !await parent.IsEnabledAsync())
        {
            return false;
        }

        await parent.HoverAsync();
        var direct = _page.GetByText(_selectors.Gpt56, new PageGetByTextOptions { Exact = true });
        try
        {
            await direct.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 1_000
            });
        }
        catch (TimeoutException)
        {
            await parent.ClickAsync();
        }

        return await TryClickModelOptionAsync(_selectors.Gpt56);
    }

    private async Task OpenModelMenuAsync(ILocator switcher)
    {
        const int timeout = 3_000;
        try
        {
            await switcher.ClickAsync(new LocatorClickOptions { Trial = true, Timeout = timeout });
            await switcher.ClickAsync(new LocatorClickOptions { Timeout = timeout });
        }
        catch (Exception exception) when (
            !_page.IsClosed && exception is PlaywrightException or TimeoutException)
        {
            var overlay = await FindBlockingOverlayDescriptorAsync(switcher);
            if (overlay is not null)
            {
                throw new PageOverlayBlockedException(
                    $"A visible Copilot page overlay blocks the model selector ({overlay}). No message was submitted.");
            }

            throw new ModelSelectorBlockedException(
                "The Copilot model selector is visible but not actionable. No message was submitted.");
        }
    }

    private async Task<bool> IsAnyAllowedModelOptionVisibleAsync()
    {
        foreach (var text in new[]
                 {
                     _selectors.Opus,
                     _selectors.GptParent,
                     _selectors.Gpt56,
                     _selectors.DeepThinkingChinese,
                     _selectors.DeepThinkingEnglish
                 })
        {
            if (await SingleVisibleModelOptionAsync(text) is not null) return true;
        }

        return false;
    }

    private async Task<bool> TryClickModelOptionAsync(string text)
    {
        var match = await SingleVisibleModelOptionAsync(text);
        if (match is null || !await match.IsEnabledAsync()) return false;
        await match.ClickAsync();
        return true;
    }

    private async Task<ILocator?> SingleVisibleModelOptionAsync(string text)
    {
        var locator = _page.GetByText(text, new PageGetByTextOptions { Exact = true });
        var switcherSelector = System.Text.Json.JsonSerializer.Serialize(
            string.Join(", ", _selectors.ModelSwitcher));
        ILocator? match = null;
        for (var index = 0; index < await locator.CountAsync(); index++)
        {
            var candidate = locator.Nth(index);
            if (!await candidate.IsVisibleAsync() ||
                await candidate.EvaluateAsync<bool>(
                    $"element => element.closest({switcherSelector}) !== null"))
            {
                continue;
            }

            if (match is not null) return null;
            match = candidate;
        }

        return match;
    }

    private static Task<string?> FindBlockingOverlayDescriptorAsync(ILocator target) =>
        target.EvaluateAsync<string?>(
            "element => { " +
            "const rect = element.getBoundingClientRect(); " +
            "const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2); " +
            "if (!hit || hit === element || element.contains(hit)) return null; " +
            "const overlay = hit.closest(\"[role='dialog'][aria-modal='true'], [aria-modal='true'], " +
            "[data-testid*='modal'], [class*='backdrop']\"); " +
            "if (!overlay) return null; " +
            "const clean = value => (value ?? '').replace(/\\s+/g, ' ').trim().slice(0, 120); " +
            "return [`tag=${overlay.tagName.toLowerCase()}`, `role=${clean(overlay.getAttribute('role'))}`, " +
            "`aria-modal=${clean(overlay.getAttribute('aria-modal'))}`, " +
            "`data-testid=${clean(overlay.getAttribute('data-testid'))}`, " +
            "`class=${clean(overlay.getAttribute('class'))}`].join(' '); }");

    internal async Task EnsureAuthenticatedAsync()
    {
        foreach (var selector in _selectors.LoginRequired)
        {
            var matches = _page.Locator(selector);
            var count = await matches.CountAsync();
            for (var index = 0; index < count; index++)
            {
                if (await matches.Nth(index).IsVisibleAsync())
                {
                    throw new InvalidOperationException(
                        "Microsoft 365 Copilot login is required. Sign in with the current Edge profile before retrying.");
                }
            }
        }
    }

    private async Task EnsureIdleAsync()
    {
        await ThrowIfPageErrorAsync();
        if (!await IsIdleAsync())
        {
            throw new InvalidOperationException("Copilot is currently generating or the chat surface is busy.");
        }
    }

    private async Task<bool> IsIdleAsync()
    {
        var busy = _page.Locator(_selectors.Busy);
        for (var index = 0; index < await busy.CountAsync(); index++)
        {
            if (await busy.Nth(index).IsVisibleAsync())
            {
                return false;
            }
        }

        return true;
    }

    private async Task ThrowIfPageErrorAsync()
    {
        var errors = _page.Locator("[role='alert'][data-testid*='error'], [data-testid*='error-message']");
        for (var index = 0; index < await errors.CountAsync(); index++)
        {
            var error = errors.Nth(index);
            if (await error.IsVisibleAsync())
            {
                throw new InvalidOperationException($"Copilot page error: {Normalize(await error.InnerTextAsync())}");
            }
        }
    }

    private async Task<ILocator> FindUniqueVisibleAsync(string description, IEnumerable<string> selectors)
    {
        foreach (var selector in selectors)
        {
            var match = await SingleVisibleAsync(_page.Locator(selector));
            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"Could not identify exactly one visible {description}.");
    }

    private async Task<ILocator> WaitForUniqueVisibleAsync(
        string description,
        IEnumerable<string> selectors)
    {
        var deadline = DateTime.UtcNow.Add(SurfaceReadyTimeout);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var selector in selectors)
            {
                var match = await SingleVisibleAsync(_page.Locator(selector));
                if (match is not null) return match;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Could not identify exactly one visible {description}.");
    }

    private static async Task<bool> MessageAdvancedAsync(
        ILocator messages,
        int countBefore,
        string? lastBefore) =>
        await messages.CountAsync() > countBefore ||
        !string.Equals(
            await ReadLastMessageFingerprintAsync(messages),
            lastBefore,
            StringComparison.Ordinal);

    private static async Task<int> ObservedMessageDeltaAsync(
        ILocator messages,
        int countBefore,
        string? lastBefore)
    {
        var countAfter = await messages.CountAsync();
        if (countAfter > countBefore)
        {
            return countAfter - countBefore;
        }

        return string.Equals(
            await ReadLastMessageFingerprintAsync(messages),
            lastBefore,
            StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static async Task<string?> ReadLastMessageFingerprintAsync(ILocator messages) =>
        await messages.CountAsync() == 0
            ? null
            : Normalize(await messages.Last.InnerTextAsync());

    private Task<bool> IsExactTextVisibleAsync(string text) =>
        IsSingleVisibleAsync(_page.GetByText(text, new PageGetByTextOptions { Exact = true }));

    private static async Task<bool> IsSingleVisibleAsync(ILocator locator) =>
        await SingleVisibleAsync(locator) is not null;

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

    private static Task<string> ReadComposerTextAsync(ILocator composer) =>
        composer.EvaluateAsync<string>(
            "element => element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement " +
            "? element.value : (element.innerText ?? '')");

    private static string CanonicalComposerText(string value) =>
        value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .TrimEnd('\n', '\u200B', '\u200C', '\u200D', '\uFEFF');

    private static bool ModelReadbackMatches(string selected, string readback) => selected switch
    {
        "Opus" => readback.Contains("Opus", StringComparison.OrdinalIgnoreCase),
        "GPT 5.6 Think deeper" => readback.Contains("GPT 5.6", StringComparison.OrdinalIgnoreCase),
        _ => readback.Contains("深度思考", StringComparison.OrdinalIgnoreCase) ||
             readback.Contains("Think deeper", StringComparison.OrdinalIgnoreCase)
    };

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

            await Task.Delay(50);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
