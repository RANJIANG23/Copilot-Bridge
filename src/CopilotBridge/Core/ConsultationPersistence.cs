namespace CopilotBridge.Core;

internal sealed record ConsultationPersistenceResult(
    ConversationDocument? Conversation,
    string? WarningCode);

internal sealed class ConsultationPersistence
{
    private readonly SettingsStore _settingsStore;
    private readonly ConsultationStateStore _stateStore;

    internal ConsultationPersistence(
        SettingsStore settingsStore,
        ConsultationStateStore stateStore)
    {
        _settingsStore = settingsStore;
        _stateStore = stateStore;
    }

    internal async Task<bool> IsStateStaleAsync(
        ConversationDocument? conversation,
        ConsultationRecord? state,
        BridgeSettings settings,
        CollaborationMode mode,
        CancellationToken cancellationToken)
    {
        if (conversation is not null)
        {
            return CollaborationBudgetOptions.RecordedTurns(conversation, mode) > (state?.TurnCount ?? 0);
        }

        if (state is null || !settings.StoreConversationContent) return false;
        var conversationUrl = mode == CollaborationMode.Review
            ? state.EvidenceConversationUrl ?? state.ComplexityConversationUrl
            : state.PrimaryConversationUrl;
        if (string.IsNullOrWhiteSpace(conversationUrl)) return false;
        var workspace = new ConversationWorkspaceStore(settings.ConversationWorkspaceDirectory);
        var document = await workspace.FindByCopilotConversationUrlAsync(conversationUrl, cancellationToken);
        return document is not null &&
               CollaborationBudgetOptions.RecordedTurns(document, mode) > state.TurnCount;
    }

    internal async Task<ConsultationPersistenceResult> SaveCompletedAsync(
        string id,
        ConversationDocument? conversation,
        CollaborationContext context,
        CollaborationRunResult result,
        BridgeSettings settings,
        CancellationToken cancellationToken)
    {
        var warning = await SaveRecordSafelyAsync(
            id,
            new ConsultationRecord
            {
                Mode = ModeName(context.Mode),
                TurnCount = result.TurnCount,
                TurnBudget = context.TurnBudget,
                PrimaryConversationUrl = result.PrimaryConversationUrl,
                ComplexityConversationUrl = result.ComplexityConversationUrl,
                EvidenceConversationUrl = result.EvidenceConversationUrl,
                Status = "completed",
                LastModel = result.Responses.Last().Result.Model
            },
            cancellationToken);

        if (result.PrimaryConversationUrl is not null &&
            !string.Equals(settings.BoundConversationUrl, result.PrimaryConversationUrl, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _settingsStore.SaveAsync(
                    settings with { BoundConversationUrl = result.PrimaryConversationUrl },
                    cancellationToken);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write("settings_persistence_failed", exception);
                warning ??= "consultation_persistence_failed";
            }
        }

        try
        {
            conversation = await SaveWorkspaceRunAsync(
                ModeName(context.Mode), result, settings, conversation, cancellationToken);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("conversation_persistence_failed", exception);
            warning ??= "consultation_persistence_failed";
        }

        return new ConsultationPersistenceResult(conversation, warning);
    }

    internal async Task<ConversationDocument?> SaveInterruptedAsync(
        string id,
        ConversationDocument? conversation,
        CollaborationContext context,
        string status,
        BridgeSettings settings,
        string? currentUrl,
        PartialReviewException? partialReview = null,
        bool partialReviewTimedOut = false,
        CancellationToken cancellationToken = default)
    {
        var record = new ConsultationRecord
        {
            Mode = ModeName(context.Mode),
            TurnCount = context.TurnCount + 1,
            TurnBudget = context.TurnBudget,
            PrimaryConversationUrl = context.Mode == CollaborationMode.Review
                ? context.PrimaryConversationUrl
                : currentUrl ?? context.PrimaryConversationUrl,
            ComplexityConversationUrl = partialReview?.Completed.Result.ConversationUrl ??
                                        (context.Mode == CollaborationMode.Review
                                            ? currentUrl ?? context.ComplexityConversationUrl
                                            : context.ComplexityConversationUrl),
            EvidenceConversationUrl = partialReview is not null
                ? currentUrl ?? context.EvidenceConversationUrl
                : context.EvidenceConversationUrl,
            Status = status,
            LastModel = partialReview?.Completed.Result.Model
        };
        await SaveRecordSafelyAsync(id, record, cancellationToken);

        try
        {
            if (partialReview is not null)
            {
                var partialResult = new CollaborationRunResult(
                    [partialReview.Completed],
                    context.TurnCount + 1,
                    null,
                    partialReview.Completed.Result.ConversationUrl,
                    record.EvidenceConversationUrl);
                conversation = await SaveWorkspaceRunAsync(
                    ModeName(context.Mode), partialResult, settings, conversation, cancellationToken);
                if (conversation is not null && partialReviewTimedOut)
                {
                    var workspace = new ConversationWorkspaceStore(settings.ConversationWorkspaceDirectory);
                    conversation = await workspace.AppendIncompleteDeliveryAsync(
                        conversation,
                        partialReview.FailedRequestMarkdown,
                        "reply_timeout",
                        "evidence",
                        record.EvidenceConversationUrl,
                        cancellationToken,
                        collaborationMode: ModeName(context.Mode));
                }
            }
            else if (status == "reply_timeout")
            {
                var request = context.Mode == CollaborationMode.Review
                    ? CollaborationRunner.ComplexityRequestMarkdown(context.Prompt)
                    : context.Prompt;
                conversation = await SaveWorkspaceIncompleteDeliveryAsync(
                    ModeName(context.Mode),
                    request,
                    context.Mode == CollaborationMode.Review ? "complexity" : "primary",
                    currentUrl ?? context.PrimaryConversationUrl,
                    settings,
                    conversation,
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("conversation_persistence_failed", exception);
        }

        return conversation;
    }

    internal static async Task<ConversationDocument?> SaveWorkspaceRunAsync(
        string mode,
        CollaborationRunResult result,
        BridgeSettings settings,
        ConversationDocument? conversation = null,
        CancellationToken cancellationToken = default)
    {
        if (!settings.StoreConversationContent) return conversation;
        var workspace = new ConversationWorkspaceStore(settings.ConversationWorkspaceDirectory);
        var conversationUrl = result.PrimaryConversationUrl ??
                              result.Responses.LastOrDefault()?.Result.ConversationUrl;
        conversation ??= string.IsNullOrWhiteSpace(conversationUrl)
            ? null
            : await workspace.FindByCopilotConversationUrlAsync(conversationUrl, cancellationToken);
        if (conversation is null)
        {
            conversation = await workspace.CreateConversationAsync(
                ConversationWorkspaceStore.StandaloneProjectId,
                cancellationToken: cancellationToken);
            conversation = conversation with { Mode = mode };
        }

        return await workspace.AppendRunAsync(
            conversation, result, cancellationToken, collaborationMode: mode);
    }

    private async Task<string?> SaveRecordSafelyAsync(
        string id,
        ConsultationRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            await _stateStore.SaveAsync(id, record, cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("consultation_persistence_failed", exception);
            return "consultation_persistence_failed";
        }
    }

    private static async Task<ConversationDocument?> SaveWorkspaceIncompleteDeliveryAsync(
        string mode,
        string requestMarkdown,
        string reviewer,
        string? conversationUrl,
        BridgeSettings settings,
        ConversationDocument? conversation,
        CancellationToken cancellationToken)
    {
        if (!settings.StoreConversationContent) return conversation;
        var workspace = new ConversationWorkspaceStore(settings.ConversationWorkspaceDirectory);
        conversation ??= string.IsNullOrWhiteSpace(conversationUrl)
            ? null
            : await workspace.FindByCopilotConversationUrlAsync(conversationUrl, cancellationToken);
        if (conversation is null)
        {
            conversation = await workspace.CreateConversationAsync(
                ConversationWorkspaceStore.StandaloneProjectId,
                cancellationToken: cancellationToken);
            conversation = conversation with { Mode = mode };
        }

        return await workspace.AppendIncompleteDeliveryAsync(
            conversation,
            requestMarkdown,
            "reply_timeout",
            reviewer,
            conversationUrl,
            cancellationToken,
            collaborationMode: mode);
    }

    private static string ModeName(CollaborationMode mode) => mode.ToString().ToLowerInvariant();
}
