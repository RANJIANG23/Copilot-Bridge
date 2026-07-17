using CopilotBridge.Core;
using Microsoft.Playwright;

namespace CopilotBridge.Browser;

internal sealed record PageTurnResult(
    string ReplyMarkdown,
    string ConversationUrl,
    int UserMessageDelta,
    int AssistantMessageDelta);

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

internal sealed class CopilotPageDriver
{
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
        var switcher = await FindUniqueVisibleAsync("model switcher", _selectors.ModelSwitcher);
        return Normalize(await switcher.InnerTextAsync());
    }

    internal async Task<string> SelectAllowedModelAsync()
    {
        var switcher = await FindUniqueVisibleAsync("model switcher", _selectors.ModelSwitcher);
        await switcher.ClickAsync();
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

        string selected;
        if (await TryClickExactAsync(_selectors.Opus))
        {
            selected = _selectors.Opus;
        }
        else if (await TrySelectGpt56Async())
        {
            selected = _selectors.Gpt56;
        }
        else if (await TryClickExactAsync(_selectors.DeepThinkingChinese) ||
                 await TryClickExactAsync(_selectors.DeepThinkingEnglish))
        {
            selected = _selectors.DeepThinkingChinese;
        }
        else
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
                async () => await users.CountAsync() > userCountBefore || await IsExactTextVisibleAsync(prompt),
                TimeSpan.FromSeconds(15),
                "The page did not expose a new user message after the single send click.");
            userMessageVerified = true;
            await WaitUntilAsync(
                async () =>
                {
                    await ThrowIfPageErrorAsync();
                    return await assistants.CountAsync() > assistantCountBefore;
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
                await users.CountAsync() - userCountBefore,
                await assistants.CountAsync() - assistantCountBefore);
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
        var direct = _page.GetByText(_selectors.Gpt56, new PageGetByTextOptions { Exact = true });
        if (await TryClickSingleEnabledAsync(direct))
        {
            return true;
        }

        var parent = await SingleVisibleAsync(
            _page.GetByText(_selectors.GptParent, new PageGetByTextOptions { Exact = true }));
        if (parent is null || !await parent.IsEnabledAsync())
        {
            return false;
        }

        await parent.HoverAsync();
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

        return await TryClickSingleEnabledAsync(direct);
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

    private Task<bool> IsExactTextVisibleAsync(string text) =>
        IsSingleVisibleAsync(_page.GetByText(text, new PageGetByTextOptions { Exact = true }));

    private Task<bool> TryClickExactAsync(string text) =>
        TryClickSingleEnabledAsync(_page.GetByText(text, new PageGetByTextOptions { Exact = true }));

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
