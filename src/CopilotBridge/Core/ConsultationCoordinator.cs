using CopilotBridge.Browser;
using Microsoft.Playwright;

namespace CopilotBridge.Core;

internal sealed record AssistRequest(string Prompt, string? ConversationUrl = null);

internal sealed record AssistResult(
    string Model,
    string ReplyMarkdown,
    string ConversationUrl,
    int UserMessageDelta,
    int AssistantMessageDelta,
    bool CanRetrySafely);

internal sealed class ConsultationCoordinator
{
    private readonly BridgeSettings _settings;
    private readonly ProviderSelectors _selectors;

    internal ConsultationCoordinator(BridgeSettings settings, ProviderSelectors selectors)
    {
        _settings = settings;
        _selectors = selectors;
    }

    internal async Task<AssistResult> AssistAsync(
        AssistRequest request,
        string? endpointOverride = null)
    {
        await using var session = await EdgeSessionAdapter.ConnectAsync(
            _settings,
            _selectors,
            endpointOverride);

        return await AssistOnPageAsync(session.Page, request);
    }

    internal async Task<AssistResult> AssistOnPageAsync(IPage page, AssistRequest request)
    {

        if (request.ConversationUrl is not null &&
            !page.Url.Equals(request.ConversationUrl, StringComparison.OrdinalIgnoreCase))
        {
            ValidateConversationUrl(request.ConversationUrl);
            await page.GotoAsync(request.ConversationUrl);
        }

        var driver = new CopilotPageDriver(page, _selectors, _settings);
        var model = await driver.SelectAllowedModelAsync();
        var turn = await driver.SendAndReadAsync(request.Prompt);
        if (turn.UserMessageDelta != 1 || turn.AssistantMessageDelta != 1)
        {
            throw new SubmissionUnknownException(
                $"Expected exactly one new user message and one new assistant reply, but observed " +
                $"{turn.UserMessageDelta} and {turn.AssistantMessageDelta}.");
        }

        return new AssistResult(
            model,
            turn.ReplyMarkdown,
            turn.ConversationUrl,
            turn.UserMessageDelta,
            turn.AssistantMessageDelta,
            false);
    }

    private void ValidateConversationUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(_selectors.AllowedHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/chat/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Conversation URL is outside the allowed Copilot chat origin.", nameof(value));
        }
    }
}
