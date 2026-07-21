using System.Windows;
using CopilotBridge.Core;

namespace CopilotBridge.UI;

public partial class MainWindow
{
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_statusRefreshInProgress)
        {
            ShowNotice(T("状态刷新正在完成，请稍后再试。"), NoticeKind.Info);
            return;
        }

        var prompt = TestPromptTextBox.Text.Trim();
        if (prompt.Length == 0)
        {
            ShowNotice(T("请先输入即时咨询内容。"), NoticeKind.Error);
            return;
        }

        SetBusy(true, "正在咨询 Copilot");
        try
        {
            await SaveSettingsFromControlsAsync();
            var createdForAttempt = _selectedConversation is null;
            var conversation = _selectedConversation ?? await CreateImmediateConversationAsync();
            var outcome = await _consultationCoordinator.ConsultAsync(
                _settings,
                new ConsultationCommand(
                    prompt,
                    "user_explicit",
                    NewConversation: createdForAttempt,
                    Conversation: conversation),
                async _ => (await GetSessionAsync()).Page);

            await ApplyConsultationOutcomeAsync(outcome, conversation, createdForAttempt);
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            SetBusy(false, "就绪");
        }
    }

    private async Task ApplyConsultationOutcomeAsync(
        ConsultationOutcome outcome,
        ConversationDocument attemptedConversation,
        bool createdForAttempt)
    {
        var sentOrUncertain = outcome.Status is "completed" or "reply_timeout" or "submission_unknown";
        if (sentOrUncertain)
        {
            _selectedConversation = outcome.Conversation ?? attemptedConversation;
            await RefreshWorkspaceAsync(_selectedConversation.Id);
        }
        else if (createdForAttempt && outcome.Status is "blocked" or "not_submitted")
        {
            await RemoveUnsubmittedConversationAsync(attemptedConversation);
        }

        if (outcome.Status == "completed" && outcome.Result is not null)
        {
            ApplyCompletedConsultation(outcome);
            return;
        }

        ShowNotice(ConsultationOutcomeMessage(outcome), NoticeKind.Error);
    }

    private void ApplyCompletedConsultation(ConsultationOutcome outcome)
    {
        var result = outcome.Result!;
        var last = result.Responses.Last();
        if (!string.IsNullOrWhiteSpace(outcome.BoundConversationUrl))
        {
            _settings = _settings with { BoundConversationUrl = outcome.BoundConversationUrl };
        }

        BoundUrlText.Text = _settings.BoundConversationUrl ?? T("Review 使用两个隔离会话");
        TabStatusText.Text = T("已绑定专用 Copilot 标签页");
        ModelStatusText.Text = last.Result.Model;

        var persistenceFailed = outcome.WarningCode == "consultation_persistence_failed";
        ShowNotice(
            persistenceFailed
                ? T("回复已完成，但部分本地状态未能保存；为避免状态不一致，请新建咨询。")
                : T("即时会话已保存为本地 Markdown；不会自动读取旧网页历史。"),
            persistenceFailed ? NoticeKind.Error : NoticeKind.Success);
    }

    private async Task RemoveUnsubmittedConversationAsync(ConversationDocument conversation)
    {
        try
        {
            await _workspace.DeleteConversationAsync(conversation);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("unsubmitted_conversation_cleanup_failed", exception);
        }
    }

    private string ConsultationOutcomeMessage(ConsultationOutcome outcome) => outcome.ErrorCode switch
    {
        "invalid_request" => T("请先输入即时咨询内容。"),
        "blocked_by_policy" => T("征询策略当前为“关闭”，请先在协作页调整。"),
        "busy" => T("已有一个咨询正在执行，请等待其完成。"),
        "consultation_mode_mismatch" =>
            T("当前会话的协作模式与默认设置不一致；请切回原模式或新建会话。"),
        "consultation_state_stale" =>
            T("本地会话记录比预算状态更新；为避免重复发送，请新建咨询。"),
        "turn_budget_exhausted" =>
            T("当前咨询已达到协作模式轮次预算，请新建咨询或调整新咨询的默认预算。"),
        "tab_rebind_required" => T("请先绑定一个专用 Copilot 标签页。 "),
        "remote_debugging_disabled" =>
            T("Edge 远程调试尚未开启。请在 edge://inspect 的 Remote debugging 页面允许当前浏览器实例。"),
        "partial_review" =>
            T("部分审查已经发送，状态不确定；请勿重试此咨询，请新建咨询后人工核对原对话。"),
        "reply_timeout" => Bilingual(
            "消息已经发送，但回复未在超时前完成。请打开原会话继续等待，不要重试发送。",
            "The message was sent, but the reply did not complete before timeout. Open the original conversation and do not resend."),
        "submission_unknown" => Bilingual(
            "已经尝试发送，但无法确认提交状态。请检查原会话，不要重试发送。",
            "The send was attempted, but its submission state is unknown. Inspect the original conversation and do not resend."),
        "login_required" => Bilingual(
            "请先在专用 Copilot 标签页完成 Microsoft 365 登录；本次未发送。",
            "Sign in to Microsoft 365 in the dedicated Copilot tab. Nothing was sent."),
        "no_eligible_model" => Bilingual(
            "没有可验证的允许模型；本次未发送。请检查账号模型权限。",
            "No allowed model could be verified. Nothing was sent; check the account's model access."),
        "composer_not_ready" => Bilingual(
            "Copilot 输入框尚未就绪；本次未发送。请检查专用标签页后重试。",
            "The Copilot composer is not ready. Nothing was sent; inspect the dedicated tab and retry."),
        "page_overlay_blocked" => Bilingual(
            "Copilot 页面被未知浮层遮挡；本次未发送。请手动检查专用标签页。",
            "An unknown overlay is blocking the Copilot page. Nothing was sent; inspect the dedicated tab."),
        "model_selector_blocked" => Bilingual(
            "模型选择器当前不可操作；本次未发送。请手动检查专用标签页。",
            "The model selector is not actionable. Nothing was sent; inspect the dedicated tab."),
        "consultation_not_found" => Bilingual(
            "没有找到可继续的咨询状态，请新建咨询。",
            "No resumable consultation state was found. Start a new consultation."),
        _ => Bilingual(
            $"咨询未完成（{outcome.ErrorCode ?? outcome.Status}）。请检查专用 Copilot 标签页。",
            $"The consultation did not complete ({outcome.ErrorCode ?? outcome.Status}). Inspect the dedicated Copilot tab.")
    };

    private string Bilingual(string chinese, string english) =>
        _settings.DisplayLanguage == AppLanguage.English ? english : chinese;
}
