using CopilotBridge.Browser;
using Microsoft.Playwright;

namespace CopilotBridge.Core;

internal sealed record CollaborationContext(
    string Prompt,
    CollaborationMode Mode,
    int TurnCount,
    string? PrimaryConversationUrl,
    string? ComplexityConversationUrl,
    string? EvidenceConversationUrl);

internal sealed record ReviewerResult(string Reviewer, string RequestMarkdown, AssistResult Result);

internal sealed record CollaborationRunResult(
    IReadOnlyList<ReviewerResult> Responses,
    int TurnCount,
    string? PrimaryConversationUrl,
    string? ComplexityConversationUrl,
    string? EvidenceConversationUrl);

internal sealed class PartialReviewException : Exception
{
    internal PartialReviewException(ReviewerResult completed, Exception innerException)
        : base("The complexity review completed, but the evidence review did not complete.", innerException) =>
        Completed = completed;

    internal ReviewerResult Completed { get; }
}

internal sealed class CollaborationRunner
{
    private readonly Func<AssistRequest, Task<AssistResult>> _execute;
    private readonly string _newConversationUrl;

    internal CollaborationRunner(
        BridgeSettings settings,
        ProviderSelectors selectors,
        IPage page)
    {
        var coordinator = new ConsultationCoordinator(settings, selectors);
        _execute = request => coordinator.AssistOnPageAsync(page, request);
        _newConversationUrl = $"https://{selectors.AllowedHost}/chat/";
    }

    internal CollaborationRunner(
        Func<AssistRequest, Task<AssistResult>> execute,
        string newConversationUrl = "https://m365.cloud.microsoft/chat/")
    {
        _execute = execute;
        _newConversationUrl = newConversationUrl;
    }

    internal async Task<CollaborationRunResult> RunAsync(CollaborationContext context)
    {
        return context.Mode == CollaborationMode.Review
            ? await RunReviewAsync(context)
            : await RunSingleAsync(context);
    }

    private async Task<CollaborationRunResult> RunSingleAsync(CollaborationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.PrimaryConversationUrl))
        {
            throw new InvalidOperationException("A bound Copilot conversation is required.");
        }

        var result = await _execute(new AssistRequest(context.Prompt, context.PrimaryConversationUrl));
        return new CollaborationRunResult(
            [new ReviewerResult("primary", context.Prompt, result)],
            context.TurnCount + 1,
            result.ConversationUrl,
            null,
            null);
    }

    private async Task<CollaborationRunResult> RunReviewAsync(CollaborationContext context)
    {
        var complexityPrompt = RolePrompt(
                "Reviewer A — Complexity and boundaries",
                "Independently examine complexity, scope boundaries, and simpler alternatives.",
                context.Prompt);
        var complexity = await _execute(new AssistRequest(
            complexityPrompt,
            context.ComplexityConversationUrl ?? _newConversationUrl));
        var first = new ReviewerResult("complexity", complexityPrompt, complexity);

        try
        {
            var evidencePrompt = RolePrompt(
                    "Reviewer B — Failure modes and evidence",
                    "Independently examine failure modes, evidence quality, and verifiability.",
                    context.Prompt);
            var evidence = await _execute(new AssistRequest(
                evidencePrompt,
                context.EvidenceConversationUrl ?? _newConversationUrl));
            return new CollaborationRunResult(
                [first, new ReviewerResult("evidence", evidencePrompt, evidence)],
                context.TurnCount + 1,
                null,
                complexity.ConversationUrl,
                evidence.ConversationUrl);
        }
        catch (Exception exception)
        {
            throw new PartialReviewException(first, exception);
        }
    }

    private static string RolePrompt(string title, string role, string request) =>
        $"# Reviewer role\n{title}\n\n{role} Do not assume or mention another reviewer.\n\n# Review request\n{request}";
}
