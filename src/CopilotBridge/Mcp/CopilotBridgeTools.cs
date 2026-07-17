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
    private static readonly IReadOnlyList<string> ModelPriority =
        ["opus", "gpt_5_6_think_deeper", "deep_thinking"];

    private readonly SettingsStore _settingsStore = new();
    private readonly ConsultationStateStore _stateStore = new();
    private readonly ProviderSelectors _selectors = ProviderSelectors.Load();
    private EdgeSessionAdapter? _session;
    private string? _sessionUserDataDirectory;

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
            ModelPriority,
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
        var id = newConversation || string.IsNullOrWhiteSpace(consultationId)
            ? Guid.NewGuid().ToString("N")
            : consultationId.Trim();

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

        if (settings.CollaborationMode != CollaborationMode.Assist)
        {
            return Failure("blocked", "collaboration_mode_unavailable", id, mode, false);
        }

        using var lease = ConsultationLease.TryAcquire();
        if (lease is null)
        {
            return Failure("blocked", "busy", id, mode, true);
        }

        var conversationUrl = await ResolveConversationAsync(
            settings,
            consultationId,
            newConversation,
            cancellationToken);
        if (conversationUrl is null)
        {
            var error = string.IsNullOrWhiteSpace(consultationId)
                ? "tab_rebind_required"
                : "consultation_not_found";
            return Failure("blocked", error, id, mode, false);
        }

        try
        {
            var session = await GetSessionAsync(settings);
            var coordinator = new ConsultationCoordinator(settings, _selectors);
            var result = await coordinator.AssistOnPageAsync(
                session.Page,
                new AssistRequest(requestMarkdown, conversationUrl));

            await SaveConversationAsync(id, result.ConversationUrl, settings, cancellationToken);
            return new ConsultResponse(
                "completed",
                null,
                id,
                mode,
                [new CopilotResponse(
                    "primary",
                    EffectiveModel(result.Model),
                    result.ConversationUrl,
                    result.ReplyMarkdown)],
                false);
        }
        catch (ReplyTimeoutException exception)
        {
            DiagnosticLog.Write("reply_timeout", exception);
            await RememberCurrentConversationAsync(id, cancellationToken);
            return Failure("reply_timeout", "reply_timeout", id, mode, false);
        }
        catch (SubmissionUnknownException exception)
        {
            DiagnosticLog.Write("submission_unknown", exception);
            await RememberCurrentConversationAsync(id, cancellationToken);
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
        return _session;
    }

    private async Task<string?> ResolveConversationAsync(
        BridgeSettings settings,
        string? consultationId,
        bool newConversation,
        CancellationToken cancellationToken)
    {
        if (newConversation)
        {
            return $"https://{_selectors.AllowedHost}/chat/";
        }

        if (!string.IsNullOrWhiteSpace(consultationId))
        {
            return await _stateStore.FindConversationAsync(
                consultationId.Trim(),
                cancellationToken);
        }

        return settings.BoundConversationUrl;
    }

    private async Task SaveConversationAsync(
        string id,
        string conversationUrl,
        BridgeSettings settings,
        CancellationToken cancellationToken)
    {
        await _stateStore.SaveConversationAsync(id, conversationUrl, cancellationToken);
        if (!string.Equals(
                settings.BoundConversationUrl,
                conversationUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            await _settingsStore.SaveAsync(
                settings with { BoundConversationUrl = conversationUrl },
                cancellationToken);
        }
    }

    private async Task RememberCurrentConversationAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (_session is { Page.IsClosed: false } &&
            Uri.TryCreate(_session.Page.Url, UriKind.Absolute, out var uri) &&
            uri.Host.Equals(_selectors.AllowedHost, StringComparison.OrdinalIgnoreCase))
        {
            await _stateStore.SaveConversationAsync(id, uri.AbsoluteUri, cancellationToken);
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

    private static string MapPreSubmitError(Exception exception) => exception.Message switch
    {
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) &&
                         message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) =>
            "remote_debugging_disabled",
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) =>
            "tab_rebind_required",
        var message when message.Contains("Found", StringComparison.OrdinalIgnoreCase) &&
                         message.Contains("eligible Copilot tabs", StringComparison.OrdinalIgnoreCase) =>
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
