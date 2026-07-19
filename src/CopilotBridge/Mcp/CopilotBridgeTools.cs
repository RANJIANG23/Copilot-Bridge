using System.ComponentModel;
using System.Reflection;
using CopilotBridge.Browser;
using CopilotBridge.Core;
using ModelContextProtocol.Server;

namespace CopilotBridge.Mcp;

public sealed record BridgeStatusResponse(
    string Version,
    string EdgeCdpStatus,
    bool ConversationBound,
    string LoginStatus,
    string ConsultationPolicy,
    string CollaborationMode,
    IReadOnlyList<string> ModelPriority,
    bool Busy);

public sealed record CopilotResponse(
    string Reviewer,
    string EffectiveModel,
    string ConversationUrl,
    string Markdown);

public sealed record ConsultResponse(
    string Status,
    string? ErrorCode,
    string ConsultationId,
    string CollaborationMode,
    IReadOnlyList<CopilotResponse> Responses,
    bool CanRetrySafely);

internal sealed class CopilotBridgeTools : IAsyncDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ConsultationStateStore _stateStore = new();
    private readonly ProviderSelectors _selectors = ProviderSelectors.Load();
    private readonly string _serverInstanceId = Guid.NewGuid().ToString("N");
    private EdgeSessionAdapter? _session;
    private string? _sessionUserDataDirectory;

    internal string ServerInstanceId => _serverInstanceId;

    [McpServerTool(
        Name = "copilot_bridge_status",
        Title = "Copilot Bridge Status",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Read Copilot Bridge configuration and local Edge/CDP readiness without sending a message or connecting to the browser.")]
    public async Task<BridgeStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var edgeStatus = ReadEdgeStatus(settings);
        var connected = _session is { Page.IsClosed: false };
        return new BridgeStatusResponse(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            connected ? "connected" : edgeStatus,
            !string.IsNullOrWhiteSpace(settings.BoundConversationUrl),
            connected ? "authenticated" : "not_checked",
            SnakeCase(settings.ConsultationPolicy),
            SnakeCase(settings.CollaborationMode),
            ModelPriorityOptions.Parse(settings.ModelPriority),
            ConsultationLease.IsBusy());
    }

    [McpServerTool(
        Name = "consult_copilot",
        Title = "Consult Microsoft 365 Copilot",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Send one verified Markdown request to the dedicated Microsoft 365 Copilot chat. Collaboration mode and model priority come only from GUI settings. Never retry an uncertain submission.")]
    public async Task<ConsultResponse> ConsultAsync(
        [Description("Focused Markdown context and question to send unchanged to Copilot.")]
        string requestMarkdown,
        [Description("Invocation reason: user_explicit, codex_auto, or required_checkpoint.")]
        string trigger,
        [Description("Returned consultation ID to reuse for a follow-up, or omit for a new consultation.")]
        string? consultationId = null,
        [Description("True only when the user explicitly wants to stop reusing the stored conversation and start a new chat.")]
        bool newConversation = false,
        CancellationToken cancellationToken = default)
    {
        var requestedId = consultationId?.Trim();
        var startFresh = newConversation || string.IsNullOrWhiteSpace(requestedId);
        var id = startFresh
            ? Guid.NewGuid().ToString("N")
            : requestedId!;

        if (string.IsNullOrWhiteSpace(requestMarkdown))
        {
            return Failure("not_submitted", "invalid_request", id, "assist", true);
        }

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var mode = SnakeCase(settings.CollaborationMode);
        var policyError = ValidatePolicy(settings, trigger);
        if (policyError is not null)
        {
            return Failure("blocked", policyError, id, mode, false);
        }

        using var lease = ConsultationLease.TryAcquire();
        if (lease is null)
        {
            return Failure("blocked", "busy", id, mode, true);
        }

        var existing = startFresh
            ? null
            : await _stateStore.FindAsync(id, cancellationToken);
        if (!startFresh && existing is null)
        {
            return Failure("blocked", "consultation_not_found", id, mode, false);
        }

        if (existing is not null && !existing.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("blocked", "consultation_mode_mismatch", id, mode, false);
        }

        if (settings.CollaborationMode != CollaborationMode.Review &&
            existing is null &&
            string.IsNullOrWhiteSpace(settings.BoundConversationUrl))
        {
            return Failure("blocked", "tab_rebind_required", id, mode, false);
        }

        var primaryUrl = ResolvePrimaryConversationUrl(
            settings.CollaborationMode,
            existing?.PrimaryConversationUrl,
            settings.BoundConversationUrl,
            startFresh,
            _selectors.AllowedHost);
        if (settings.CollaborationMode != CollaborationMode.Review &&
            string.IsNullOrWhiteSpace(primaryUrl))
        {
            return Failure("blocked", "tab_rebind_required", id, mode, false);
        }

        var context = new CollaborationContext(
            requestMarkdown,
            settings.CollaborationMode,
            existing?.TurnCount ?? 0,
            primaryUrl,
            existing?.ComplexityConversationUrl,
            existing?.EvidenceConversationUrl);

        try
        {
            var session = await GetSessionAsync(settings);
            var result = await new CollaborationRunner(settings, _selectors, session.Page)
                .RunAsync(context);
            await SaveRunAsync(id, mode, result, settings, cancellationToken);
            return new ConsultResponse(
                "completed",
                null,
                id,
                mode,
                result.Responses.Select(response => new CopilotResponse(
                    response.Reviewer,
                    EffectiveModel(response.Result.Model),
                    response.Result.ConversationUrl,
                    response.Result.ReplyMarkdown)).ToArray(),
                false);
        }
        catch (TurnBudgetExceededException exception)
        {
            DiagnosticLog.Write("turn_budget_exhausted", exception);
            return Failure("blocked", "turn_budget_exhausted", id, mode, false);
        }
        catch (PartialReviewException exception)
        {
            DiagnosticLog.Write("submission_unknown", exception);
            await _stateStore.SaveAsync(
                id,
                new ConsultationRecord
                {
                    Mode = mode,
                    TurnCount = context.TurnCount + 1,
                    ComplexityConversationUrl = exception.Completed.Result.ConversationUrl,
                    EvidenceConversationUrl = context.EvidenceConversationUrl,
                    Status = "submission_unknown",
                    LastModel = EffectiveModel(exception.Completed.Result.Model)
                },
                cancellationToken);
            return Failure("submission_unknown", "partial_review", id, mode, false);
        }
        catch (ReplyTimeoutException exception)
        {
            DiagnosticLog.Write("reply_timeout", exception);
            return Failure("reply_timeout", "reply_timeout", id, mode, false);
        }
        catch (SubmissionUnknownException exception)
        {
            DiagnosticLog.Write("submission_unknown", exception);
            return Failure("submission_unknown", "submission_unknown", id, mode, false);
        }
        catch (Exception exception)
        {
            var errorCode = MapPreSubmitError(exception);
            DiagnosticLog.Write(errorCode, exception);
            return Failure("not_submitted", errorCode, id, mode, true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<EdgeSessionAdapter> GetSessionAsync(BridgeSettings settings)
    {
        if (_session is not null &&
            !_session.Page.IsClosed &&
            string.Equals(
                _sessionUserDataDirectory,
                settings.EdgeUserDataDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog.WriteInfo(
                "mcp_cdp_session_reused",
                $"process_id={Environment.ProcessId} server_instance={_serverInstanceId}");
            return _session;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await EdgeSessionAdapter.ConnectAsync(
            settings,
            _selectors,
            timeoutMilliseconds: 120_000);
        _sessionUserDataDirectory = settings.EdgeUserDataDirectory;
        DiagnosticLog.WriteInfo(
            "mcp_cdp_session_created",
            $"process_id={Environment.ProcessId} server_instance={_serverInstanceId}");
        return _session;
    }

    private async Task SaveRunAsync(
        string id,
        string mode,
        CollaborationRunResult result,
        BridgeSettings settings,
        CancellationToken cancellationToken)
    {
        var last = result.Responses.Last();
        await _stateStore.SaveAsync(
            id,
            new ConsultationRecord
            {
                Mode = mode,
                TurnCount = result.TurnCount,
                PrimaryConversationUrl = result.PrimaryConversationUrl,
                ComplexityConversationUrl = result.ComplexityConversationUrl,
                EvidenceConversationUrl = result.EvidenceConversationUrl,
                Status = "completed",
                LastModel = EffectiveModel(last.Result.Model)
            },
            cancellationToken);

        if (result.PrimaryConversationUrl is not null &&
            !string.Equals(
                settings.BoundConversationUrl,
                result.PrimaryConversationUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            await _settingsStore.SaveAsync(
                settings with { BoundConversationUrl = result.PrimaryConversationUrl },
                cancellationToken);
        }
    }

    internal static string? ValidatePolicy(BridgeSettings settings, string trigger)
    {
        if (settings.ConsultationPolicy == ConsultationPolicy.Disabled)
        {
            return "blocked_by_policy";
        }

        if (trigger is not ("user_explicit" or "codex_auto" or "required_checkpoint"))
        {
            return "invalid_trigger";
        }

        return settings.ConsultationPolicy switch
        {
            ConsultationPolicy.ManualOnly when trigger != "user_explicit" => "blocked_by_policy",
            _ => null
        };
    }

    internal static string? ResolvePrimaryConversationUrl(
        CollaborationMode mode,
        string? existingConversationUrl,
        string? boundConversationUrl,
        bool startFresh,
        string allowedHost) => mode switch
    {
        CollaborationMode.Review => null,
        _ when startFresh => $"https://{allowedHost}/chat/",
        _ => existingConversationUrl ?? boundConversationUrl
    };

    private static ConsultResponse Failure(
        string status,
        string errorCode,
        string consultationId,
        string collaborationMode,
        bool canRetrySafely) => new(
            status,
            errorCode,
            consultationId,
            collaborationMode,
            [],
            canRetrySafely);

    private static string ReadEdgeStatus(BridgeSettings settings)
    {
        try
        {
            EdgeSessionAdapter.ResolveEndpoint(settings.EdgeUserDataDirectory);
            return "endpoint_ready";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException)
        {
            return "remote_debugging_unavailable";
        }
    }

    private static string EffectiveModel(string model) => model switch
    {
        "Opus" => "opus",
        "GPT 5.6 Think deeper" => "gpt_5_6_think_deeper",
        _ => "deep_thinking"
    };

    internal static string MapPreSubmitError(Exception exception) => exception.Message switch
    {
        var message when message.Contains("login is required", StringComparison.OrdinalIgnoreCase) =>
            "login_required",
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when (message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                          message.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase)) &&
                         message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) =>
            "tab_rebind_required",
        var message when message.Contains("Found", StringComparison.OrdinalIgnoreCase) &&
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

    private static string SnakeCase<T>(T value) where T : struct, Enum =>
        value.ToString() switch
        {
            "ManualOnly" => "manual_only",
            "CodexMayConsult" => "codex_may_consult",
            "RequiredForKeyDesign" => "required_for_key_design",
            _ => value.ToString().ToLowerInvariant()
        };
}
