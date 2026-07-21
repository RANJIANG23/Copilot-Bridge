namespace CopilotBridge.Core;

internal sealed record CollaborationContext(
    string Prompt,
    CollaborationMode Mode,
    int TurnCount,
    string? PrimaryConversationUrl,
    string? ComplexityConversationUrl,
    string? EvidenceConversationUrl,
    int TurnBudget = 0);

internal sealed record ReviewerResult(string Reviewer, string RequestMarkdown, AssistResult Result);

internal sealed record CollaborationRunResult(
    IReadOnlyList<ReviewerResult> Responses,
    int TurnCount,
    string? PrimaryConversationUrl,
    string? ComplexityConversationUrl,
    string? EvidenceConversationUrl);

internal sealed class PartialReviewException : Exception
{
    internal PartialReviewException(
        ReviewerResult completed,
        string failedRequestMarkdown,
        Exception innerException)
        : base("The complexity review completed, but the evidence review did not complete.", innerException)
    {
        Completed = completed;
        FailedRequestMarkdown = failedRequestMarkdown;
    }

    internal ReviewerResult Completed { get; }
    internal string FailedRequestMarkdown { get; }
}

internal sealed class TurnBudgetExceededException : Exception
{
    internal TurnBudgetExceededException(CollaborationMode mode, int turnBudget)
        : base($"The {mode} consultation has reached its {turnBudget}-turn budget.")
    {
        Mode = mode;
        TurnBudget = turnBudget;
    }

    internal CollaborationMode Mode { get; }
    internal int TurnBudget { get; }
}

internal sealed class CollaborationRunner
{
    private readonly Func<AssistRequest, Task<AssistResult>> _execute;
    private readonly string _newConversationUrl;

    internal CollaborationRunner(
        Func<AssistRequest, Task<AssistResult>> execute,
        string newConversationUrl = "https://m365.cloud.microsoft/chat/")
    {
        _execute = execute;
        _newConversationUrl = newConversationUrl;
    }

    internal async Task<CollaborationRunResult> RunAsync(CollaborationContext context)
    {
        EnsureWithinTurnBudget(context);

        return context.Mode == CollaborationMode.Review
            ? await RunReviewAsync(context)
            : await RunSingleAsync(context);
    }

    internal static void EnsureWithinTurnBudget(CollaborationContext context)
    {
        var turnBudget = context.TurnBudget > 0
            ? context.TurnBudget
            : context.Mode switch
            {
                CollaborationMode.Outsource => CollaborationBudgetOptions.DefaultOutsource,
                CollaborationMode.Review => CollaborationBudgetOptions.DefaultReview,
                _ => CollaborationBudgetOptions.DefaultAssist
            };
        if (context.TurnCount >= turnBudget)
        {
            throw new TurnBudgetExceededException(context.Mode, turnBudget);
        }
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
        var complexityPrompt = ComplexityRequestMarkdown(context.Prompt);
        var complexity = await _execute(new AssistRequest(
            complexityPrompt,
            context.ComplexityConversationUrl ?? _newConversationUrl));
        var first = new ReviewerResult("complexity", complexityPrompt, complexity);

        try
        {
            var evidencePrompt = EvidenceRequestMarkdown(context.Prompt);
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
            throw new PartialReviewException(first, EvidenceRequestMarkdown(context.Prompt), exception);
        }
    }

    internal static string ComplexityRequestMarkdown(string request) => RolePrompt(
        "Reviewer A — Complexity and boundaries",
        "Independently examine complexity, scope boundaries, and simpler alternatives.",
        request);

    internal static string EvidenceRequestMarkdown(string request) => RolePrompt(
        "Reviewer B — Failure modes and evidence",
        "Independently examine failure modes, evidence quality, and verifiability.",
        request);

    private static string RolePrompt(string title, string role, string request) =>
        $"# Reviewer role\n{title}\n\n{role} Do not assume or mention another reviewer.\n\n# Review request\n{request}";
}
