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

internal sealed record ConsultationCommand(
    string RequestMarkdown,
    string Trigger,
    string? RequestedConsultationId = null,
    bool NewConversation = false,
    ConversationDocument? Conversation = null);

internal sealed record ConsultationOutcome(
    string Status,
    string? ErrorCode,
    string ConsultationId,
    CollaborationMode Mode,
    CollaborationRunResult? Result,
    bool CanRetrySafely,
    string RetryAction,
    string? WarningCode = null,
    ConversationDocument? Conversation = null,
    string? BoundConversationUrl = null);

internal sealed class ConsultationCoordinator
{
    private readonly ConsultationStateStore _stateStore;
    private readonly ProviderSelectors _selectors;
    private readonly ConsultationPersistence _persistence;
    private readonly string? _leasePath;

    internal ConsultationCoordinator(
        SettingsStore settingsStore,
        ConsultationStateStore stateStore,
        ProviderSelectors selectors,
        string? leasePath = null)
    {
        _stateStore = stateStore;
        _selectors = selectors;
        _persistence = new ConsultationPersistence(settingsStore, stateStore);
        _leasePath = leasePath;
    }

    internal async Task<ConsultationOutcome> ConsultAsync(
        BridgeSettings settings,
        ConsultationCommand command,
        Func<CancellationToken, Task<IPage>> acquirePageAsync,
        CancellationToken cancellationToken = default)
    {
        var requestedId = command.RequestedConsultationId?.Trim();
        var startFresh = command.NewConversation ||
                         (command.Conversation is null && string.IsNullOrWhiteSpace(requestedId));
        var id = command.Conversation?.Id ??
                 (startFresh ? Guid.NewGuid().ToString("N") : requestedId!);
        var mode = settings.CollaborationMode;

        if (string.IsNullOrWhiteSpace(command.RequestMarkdown))
        {
            return Failure("not_submitted", "invalid_request", id, mode, true, startFresh);
        }

        var policyError = ValidatePolicy(settings, command.Trigger);
        if (policyError is not null)
        {
            return Failure("blocked", policyError, id, mode, false, startFresh);
        }

        using var lease = ConsultationLease.TryAcquire(_leasePath);
        if (lease is null)
        {
            return Failure("blocked", "busy", id, mode, true, startFresh);
        }

        IPage? page = null;
        CollaborationContext? context = null;
        try
        {
            var existing = startFresh ? null : await _stateStore.FindAsync(id, cancellationToken);
            if (!startFresh && command.Conversation is null && existing is null)
            {
                return Failure("blocked", "consultation_not_found", id, mode, false, startFresh);
            }

            if (existing is not null && !existing.Mode.Equals(ModeName(mode), StringComparison.OrdinalIgnoreCase))
            {
                return Failure("blocked", "consultation_mode_mismatch", id, mode, false, startFresh);
            }

            if (await _persistence.IsStateStaleAsync(
                    command.Conversation,
                    existing,
                    settings,
                    mode,
                    cancellationToken))
            {
                return Failure("blocked", "consultation_state_stale", id, mode, false, startFresh);
            }

            if (mode != CollaborationMode.Review &&
                startFresh &&
                string.IsNullOrWhiteSpace(settings.BoundConversationUrl))
            {
                return Failure("blocked", "tab_rebind_required", id, mode, false, startFresh);
            }

            var primaryUrl = ResolvePrimaryConversationUrl(
                mode,
                existing?.PrimaryConversationUrl ?? command.Conversation?.CopilotConversationUrl,
                settings.BoundConversationUrl,
                startFresh,
                _selectors.NewChatUrlFor(settings.BoundConversationUrl));
            if (mode != CollaborationMode.Review && string.IsNullOrWhiteSpace(primaryUrl))
            {
                return Failure("blocked", "tab_rebind_required", id, mode, false, startFresh);
            }

            context = new CollaborationContext(
                command.RequestMarkdown,
                mode,
                existing?.TurnCount ?? 0,
                primaryUrl,
                existing?.ComplexityConversationUrl,
                existing?.EvidenceConversationUrl,
                existing is { TurnBudget: > 0 }
                    ? existing.TurnBudget
                    : CollaborationBudgetOptions.ForMode(settings, mode));

            CollaborationRunner.EnsureWithinTurnBudget(context);
            page = await acquirePageAsync(cancellationToken);
            var turnExecutor = new CopilotTurnExecutor(settings, _selectors);
            var result = await new CollaborationRunner(
                    request => turnExecutor.AssistOnPageAsync(page, request),
                    _selectors.NewChatUrlFor(page.Url))
                .RunAsync(context);

            var persisted = await _persistence.SaveCompletedAsync(
                id,
                command.Conversation,
                context,
                result,
                settings,
                cancellationToken);
            return new ConsultationOutcome(
                "completed",
                null,
                id,
                mode,
                result,
                false,
                "none",
                persisted.WarningCode,
                persisted.Conversation,
                result.PrimaryConversationUrl ?? settings.BoundConversationUrl);
        }
        catch (TurnBudgetExceededException exception)
        {
            DiagnosticLog.Write("turn_budget_exhausted", exception);
            return Failure("blocked", "turn_budget_exhausted", id, mode, false, startFresh);
        }
        catch (PartialReviewException exception) when (context is not null)
        {
            DiagnosticLog.Write("submission_unknown", exception);
            var conversation = await _persistence.SaveInterruptedAsync(
                id,
                command.Conversation,
                context,
                "submission_unknown",
                settings,
                CurrentConversationUrl(page),
                exception,
                exception.InnerException is ReplyTimeoutException,
                cancellationToken);
            return Failure("submission_unknown", "partial_review", id, mode, false, startFresh) with
            {
                Conversation = conversation
            };
        }
        catch (ReplyTimeoutException exception) when (context is not null)
        {
            DiagnosticLog.Write("reply_timeout", exception);
            var conversation = await _persistence.SaveInterruptedAsync(
                id,
                command.Conversation,
                context,
                "reply_timeout",
                settings,
                CurrentConversationUrl(page),
                cancellationToken: cancellationToken);
            return Failure("reply_timeout", "reply_timeout", id, mode, false, startFresh) with
            {
                Conversation = conversation
            };
        }
        catch (SubmissionUnknownException exception) when (context is not null)
        {
            DiagnosticLog.Write("submission_unknown", exception);
            var conversation = await _persistence.SaveInterruptedAsync(
                id,
                command.Conversation,
                context,
                "submission_unknown",
                settings,
                CurrentConversationUrl(page),
                cancellationToken: cancellationToken);
            return Failure("submission_unknown", "submission_unknown", id, mode, false, startFresh) with
            {
                Conversation = conversation
            };
        }
        catch (Exception exception)
        {
            var errorCode = MapPreSubmitError(exception);
            DiagnosticLog.Write(errorCode, exception);
            return Failure("not_submitted", errorCode, id, mode, true, startFresh);
        }
    }

    internal static string? ValidatePolicy(BridgeSettings settings, string trigger)
    {
        if (settings.ConsultationPolicy == ConsultationPolicy.Disabled) return "blocked_by_policy";
        if (trigger is not ("user_explicit" or "codex_auto" or "required_checkpoint"))
        {
            return "invalid_trigger";
        }

        return settings.ConsultationPolicy == ConsultationPolicy.ManualOnly && trigger != "user_explicit"
            ? "blocked_by_policy"
            : null;
    }

    internal static string? ResolvePrimaryConversationUrl(
        CollaborationMode mode,
        string? existingConversationUrl,
        string? boundConversationUrl,
        bool startFresh,
        string newConversationUrl) => mode switch
        {
            CollaborationMode.Review => null,
            _ when startFresh => newConversationUrl,
            _ => existingConversationUrl ?? boundConversationUrl
        };

    internal static string RetryActionFor(bool canRetrySafely, bool startFresh) =>
        !canRetrySafely
            ? "none"
            : startFresh
                ? "new_consultation"
                : "reuse_consultation";

    internal static string MapPreSubmitError(Exception exception) => exception.Message switch
    {
        _ when exception is PageOverlayBlockedException => "page_overlay_blocked",
        _ when exception is ModelSelectorBlockedException => "model_selector_blocked",
        var message when message.Contains("login is required", StringComparison.OrdinalIgnoreCase) =>
            "login_required",
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when (message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                          message.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase)) &&
                         message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("eligible Copilot tabs", StringComparison.OrdinalIgnoreCase) =>
            "tab_rebind_required",
        var message when message.Contains("has been closed", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("page closed", StringComparison.OrdinalIgnoreCase) =>
            "tab_rebind_required",
        var message when message.Contains("allowed model", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("Model readback", StringComparison.OrdinalIgnoreCase) =>
            "no_eligible_model",
        var message when message.Contains("composer", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("send button", StringComparison.OrdinalIgnoreCase) =>
            "composer_not_ready",
        _ => "not_submitted"
    };

    private static ConsultationOutcome Failure(
        string status,
        string errorCode,
        string consultationId,
        CollaborationMode mode,
        bool canRetrySafely,
        bool startFresh) => new(
            status,
            errorCode,
            consultationId,
            mode,
            null,
            canRetrySafely,
            RetryActionFor(canRetrySafely, startFresh));

    private string? CurrentConversationUrl(IPage? page) =>
        page is { IsClosed: false } && _selectors.IsAllowedChatUrl(page.Url)
            ? page.Url
            : null;

    private static string ModeName(CollaborationMode mode) => mode.ToString().ToLowerInvariant();
}
