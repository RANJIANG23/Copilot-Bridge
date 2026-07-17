using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CopilotBridge.Browser;
using CopilotBridge.Core;

namespace CopilotBridge.UI;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ProviderSelectors _selectors = ProviderSelectors.Load();
    private BridgeSettings _settings = new();
    private EdgeSessionAdapter? _session;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            ApplySettingsToControls();
            await RefreshStatusAsync();
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
            HeaderStatusText.Text = "需要检查";
        }
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page)
        {
            return;
        }

        OverviewPanel.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        CollaborationPanel.Visibility = page == "collaboration" ? Visibility.Visible : Visibility.Collapsed;
        BrowserPanel.Visibility = page == "browser" ? Visibility.Visible : Visibility.Collapsed;

        SetNavState(OverviewNav, page == "overview");
        SetNavState(CollaborationNav, page == "collaboration");
        SetNavState(BrowserNav, page == "browser");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, _session is null ? "等待 Edge 授权" : "正在绑定标签页");
        try
        {
            var session = await GetSessionAsync();
            _settings = _settings with { BoundConversationUrl = session.Page.Url };
            await _settingsStore.SaveAsync(_settings);
            BoundUrlText.Text = session.Page.Url;
            TabStatusText.Text = session.Page.Url;
            ShowNotice("已绑定当前专用 Copilot 标签页。", NoticeKind.Success);
            await ReadConnectedStatusAsync(session);
        }
        catch (Exception exception)
        {
            SetDisconnectedState();
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            SetBusy(false, "就绪");
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            await SaveSettingsFromControlsAsync();
            ShowNotice("设置已保存，将从下一次咨询开始生效。", NoticeKind.Success);
            HeaderStatusText.Text = "设置已保存";
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var prompt = TestPromptTextBox.Text.Trim();
        if (prompt.Length == 0)
        {
            ShowNotice("请先输入测试问题。", NoticeKind.Error);
            return;
        }

        SetBusy(true, "正在咨询 Copilot");
        try
        {
            await SaveSettingsFromControlsAsync();
            if (_settings.ConsultationPolicy == ConsultationPolicy.Disabled)
            {
                throw new InvalidOperationException("征询策略当前为“关闭”，请先在协作页调整。 ");
            }

            if (_settings.CollaborationMode != CollaborationMode.Assist)
            {
                throw new InvalidOperationException("当前阶段只有 Assist 可用。Outsource 与 Review 将在 Phase 5 实现。");
            }

            if (string.IsNullOrWhiteSpace(_settings.BoundConversationUrl))
            {
                throw new InvalidOperationException("请先绑定一个专用 Copilot 标签页。");
            }

            var coordinator = new ConsultationCoordinator(_settings, _selectors);
            var session = await GetSessionAsync();
            var result = await coordinator.AssistOnPageAsync(
                session.Page,
                new AssistRequest(prompt, _settings.BoundConversationUrl));

            _settings = _settings with { BoundConversationUrl = result.ConversationUrl };
            await _settingsStore.SaveAsync(_settings);
            BoundUrlText.Text = result.ConversationUrl;
            TabStatusText.Text = result.ConversationUrl;
            RecentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
            RecentModelText.Text = result.Model;
            RecentStateText.Text = result.UserMessageDelta == 1 && result.AssistantMessageDelta == 1
                ? "成功"
                : "结果需检查";
            RecentReplyText.Text = SingleLine(result.ReplyMarkdown);
            ModelStatusText.Text = result.Model;
            ShowNotice("测试咨询完成；本次正文仅显示在当前窗口，不会写入配置文件。", NoticeKind.Success);
        }
        catch (SubmissionUnknownException exception)
        {
            RecentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
            RecentStateText.Text = "发送状态未知";
            RecentReplyText.Text = "不会自动重试";
            ShowNotice(exception.Message, NoticeKind.Error);
        }
        catch (Exception exception)
        {
            RecentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
            RecentStateText.Text = "未发送";
            RecentReplyText.Text = "可在修正问题后重试";
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            SetBusy(false, "就绪");
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, _session is null ? "等待 Edge 授权" : "正在刷新状态");
        try
        {
            var session = await GetSessionAsync();
            await ReadConnectedStatusAsync(session);
            ClearNotice();
        }
        catch (Exception exception)
        {
            SetDisconnectedState();
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            SetBusy(false, EdgeStatusText.Text == "已连接" ? "Edge 已连接" : "需要设置");
        }
    }

    private async Task ReadConnectedStatusAsync(EdgeSessionAdapter session)
    {
        var driver = new CopilotPageDriver(session.Page, _selectors, _settings);
        EdgeStatusText.Text = "已连接";
        EdgeStatusDot.Fill = Brush("#31C76A");
        LoginStatusText.Text = "已登录";
        ModelStatusText.Text = await driver.ReadCurrentModelAsync();
        TabStatusText.Text = session.Page.Url;
        HeaderStatusText.Text = "Edge 已连接";
    }

    private async Task<EdgeSessionAdapter> GetSessionAsync()
    {
        if (_session is not null && !_session.Page.IsClosed)
        {
            return _session;
        }

        await ResetSessionAsync();
        _session = await EdgeSessionAdapter.ConnectAsync(
            _settings,
            _selectors,
            timeoutMilliseconds: 30_000);
        return _session;
    }

    private async Task ResetSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        await _session.DisposeAsync();
        _session = null;
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await ResetSessionAsync();
    }

    private void SetDisconnectedState()
    {
        EdgeStatusText.Text = "未连接";
        EdgeStatusDot.Fill = Brush("#E55757");
        LoginStatusText.Text = "无法确认";
        ModelStatusText.Text = "未知";
        HeaderStatusText.Text = "需要设置";
    }

    private async Task SaveSettingsFromControlsAsync()
    {
        if (!int.TryParse(MenuMinimumTextBox.Text, out var menuMinimum) || menuMinimum < 0 ||
            !int.TryParse(MenuMaximumTextBox.Text, out var menuMaximum) || menuMaximum < menuMinimum ||
            !int.TryParse(ReplyTimeoutTextBox.Text, out var replyTimeout) || replyTimeout <= 0)
        {
            throw new InvalidDataException("等待时间无效：最大等待必须大于或等于最短等待，回复超时必须大于 0。");
        }

        _settings = _settings with
        {
            MenuMinimumWaitMilliseconds = menuMinimum,
            MenuMaximumWaitMilliseconds = menuMaximum,
            ReplyTimeoutSeconds = replyTimeout,
            ConsultationPolicy = (ConsultationPolicy)Math.Max(0, PolicyComboBox.SelectedIndex),
            CollaborationMode = CollaborationMode.Assist
        };
        await _settingsStore.SaveAsync(_settings);
    }

    private void ApplySettingsToControls()
    {
        PolicyComboBox.SelectedIndex = (int)_settings.ConsultationPolicy;
        AssistRadio.IsChecked = _settings.CollaborationMode == CollaborationMode.Assist;
        MenuMinimumTextBox.Text = _settings.MenuMinimumWaitMilliseconds.ToString();
        MenuMaximumTextBox.Text = _settings.MenuMaximumWaitMilliseconds.ToString();
        ReplyTimeoutTextBox.Text = _settings.ReplyTimeoutSeconds.ToString();
        BoundUrlText.Text = _settings.BoundConversationUrl ?? "未绑定";
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatusText.Text = status;
        RefreshButton.IsEnabled = !busy;
        BindButton.IsEnabled = !busy;
        TestButton.IsEnabled = !busy;
        SaveCollaborationButton.IsEnabled = !busy;
        SaveBrowserButton.IsEnabled = !busy;
    }

    private void ShowNotice(string message, NoticeKind kind)
    {
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
        NoticeBorder.Background = Brush(kind switch
        {
            NoticeKind.Success => "#EAF7EF",
            NoticeKind.Error => "#FFF0F0",
            _ => "#FFF4E5"
        });
        NoticeBorder.BorderBrush = Brush(kind switch
        {
            NoticeKind.Success => "#B9E5C9",
            NoticeKind.Error => "#F3C0C0",
            _ => "#FFD7A3"
        });
        NoticeText.Foreground = Brush(kind switch
        {
            NoticeKind.Success => "#176B3A",
            NoticeKind.Error => "#9B2C2C",
            _ => "#7A4A0A"
        });
    }

    private void ClearNotice() => NoticeBorder.Visibility = Visibility.Collapsed;

    private static void SetNavState(Button button, bool selected)
    {
        button.Background = Brush(selected ? "#EAF3FF" : "#00FFFFFF");
        button.Foreground = Brush(selected ? "#0A6CFF" : "#4D5868");
    }

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));

    private static string FriendlyMessage(Exception exception) => exception.Message switch
    {
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) =>
            "Edge 远程调试尚未开启。请在 edge://inspect 的 Remote debugging 页面允许当前浏览器实例。",
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) =>
            "没有发现可用的 Microsoft 365 Copilot 聊天标签页。请先打开 https://m365.cloud.microsoft/chat/。",
        var message when message.Contains("Found", StringComparison.OrdinalIgnoreCase) &&
                         message.Contains("eligible Copilot tabs", StringComparison.OrdinalIgnoreCase) =>
            "发现多个 Copilot 聊天标签页。请只保留一个专用标签页后重试。",
        var message when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) &&
                         message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) =>
            "等待 Edge 允许远程访问超时。请在 Edge 中选择“允许”，然后点击刷新状态；本次运行的后续操作会复用同一连接。",
        _ => exception.Message
    };

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private enum NoticeKind
    {
        Info,
        Success,
        Error
    }
}
