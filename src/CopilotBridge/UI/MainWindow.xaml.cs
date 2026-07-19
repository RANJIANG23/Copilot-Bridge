using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotBridge.Browser;
using CopilotBridge.Core;

namespace CopilotBridge.UI;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ConsultationStateStore _stateStore = new();
    private readonly ProviderSelectors _selectors = ProviderSelectors.Load();
    private readonly DispatcherTimer _statusRefreshTimer = new();
    private BridgeSettings _settings = new();
    private ConversationWorkspaceStore _workspace = new();
    private IReadOnlyList<WorkspaceProject> _projects = [];
    private ConversationDocument? _selectedConversation;
    private string _activeProjectId = ConversationWorkspaceStore.InboxProjectId;
    private EdgeSessionAdapter? _session;
    private bool _busy;
    private Point _conversationDragStart;
    private string _activePage = "overview";
    private bool _windowIsActive;
    private int _consecutiveStatusRefreshFailures;
    private DateTimeOffset? _lastStatusRefresh;

    public MainWindow()
    {
        InitializeComponent();
        _statusRefreshTimer.Tick += StatusRefreshTimer_Tick;
        Activated += Window_Activated;
        Deactivated += Window_Deactivated;
        StateChanged += Window_StateChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _windowIsActive = IsActive;
            _settings = await _settingsStore.LoadAsync();
            _workspace = new ConversationWorkspaceStore(_settings.ConversationWorkspaceDirectory);
            ApplySettingsToControls();
            await RefreshWorkspaceAsync();
            await RefreshStatusAsync();
            ScheduleStatusRefresh();
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
            HeaderStatusText.Text = "需要检查";
        }
    }

    private async void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page) return;
        _activePage = page;
        OverviewPanel.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        CollaborationPanel.Visibility = page == "collaboration" ? Visibility.Visible : Visibility.Collapsed;
        BrowserPanel.Visibility = page == "browser" ? Visibility.Visible : Visibility.Collapsed;
        SetNavState(OverviewNav, page == "overview");
        SetNavState(HistoryNav, page == "history");
        SetNavState(CollaborationNav, page == "collaboration");
        SetNavState(BrowserNav, page == "browser");
        if (page == "history") await RefreshWorkspaceAsync();
        ScheduleStatusRefresh();
        if (page == "overview" && !_busy)
        {
            await RefreshStatusAsync(automatic: true);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
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
        finally { SetBusy(false, "就绪"); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            await SaveSettingsFromControlsAsync();
            ShowNotice("设置已保存，将从下一次咨询开始生效。", NoticeKind.Success);
            HeaderStatusText.Text = "设置已保存";
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var prompt = TestPromptTextBox.Text.Trim();
        if (prompt.Length == 0)
        {
            ShowNotice("请先输入即时咨询内容。", NoticeKind.Error);
            return;
        }

        using var lease = ConsultationLease.TryAcquire();
        if (lease is null)
        {
            ShowNotice("已有一个咨询正在执行，请等待其完成。", NoticeKind.Error);
            return;
        }

        SetBusy(true, "正在咨询 Copilot");
        try
        {
            await SaveSettingsFromControlsAsync();
            if (_settings.ConsultationPolicy == ConsultationPolicy.Disabled)
            {
                throw new InvalidOperationException("征询策略当前为“关闭”，请先在协作页调整。");
            }

            var conversation = _selectedConversation ?? await CreateImmediateConversationAsync();
            var primaryUrl = conversation.CopilotConversationUrl ?? _settings.BoundConversationUrl;
            if (_settings.CollaborationMode != CollaborationMode.Review && string.IsNullOrWhiteSpace(primaryUrl))
            {
                throw new InvalidOperationException("请先绑定一个专用 Copilot 标签页。 ");
            }

            var session = await GetSessionAsync();
            var result = await new CollaborationRunner(_settings, _selectors, session.Page)
                .RunAsync(new CollaborationContext(
                    prompt,
                    _settings.CollaborationMode,
                    0,
                    primaryUrl,
                    null,
                    null));
            var last = result.Responses.Last();
            _selectedConversation = await _workspace.AppendRunAsync(conversation, result);

            var id = _selectedConversation.Id;
            await _stateStore.SaveAsync(id, new ConsultationRecord
            {
                Mode = _settings.CollaborationMode.ToString().ToLowerInvariant(),
                TurnCount = result.TurnCount,
                PrimaryConversationUrl = result.PrimaryConversationUrl,
                ComplexityConversationUrl = result.ComplexityConversationUrl,
                EvidenceConversationUrl = result.EvidenceConversationUrl,
                Status = "completed",
                LastModel = last.Result.Model
            });

            if (result.PrimaryConversationUrl is not null)
            {
                _settings = _settings with { BoundConversationUrl = result.PrimaryConversationUrl };
                await _settingsStore.SaveAsync(_settings);
            }

            BoundUrlText.Text = _settings.BoundConversationUrl ?? "Review 使用两个隔离会话";
            TabStatusText.Text = session.Page.Url;
            ModelStatusText.Text = last.Result.Model;
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice("即时会话已保存为本地 Markdown；不会自动读取旧网页历史。", NoticeKind.Success);
        }
        catch (ReplyTimeoutException exception)
        {
            ShowNotice(exception.Message, NoticeKind.Error);
        }
        catch (SubmissionUnknownException exception)
        {
            ShowNotice(exception.Message, NoticeKind.Error);
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally { SetBusy(false, "就绪"); }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var project = await _workspace.CreateProjectAsync(NewProjectTextBox.Text);
            NewProjectTextBox.Clear();
            _activeProjectId = project.Id;
            await RefreshWorkspaceAsync();
            SelectProject(project.Id);
            ShowNotice($"已创建项目“{project.Name}”。", NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _selectedConversation = await CreateImmediateConversationAsync();
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice("已创建即时会话。输入内容后在概览页发送，正文会写入该会话。", NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not WorkspaceProject project) return;
        _activeProjectId = project.Id;
        await RefreshConversationListAsync();
    }

    private void ConversationListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _conversationDragStart = e.GetPosition(ConversationListBox);

    private void ConversationListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ConversationListBox.SelectedItem is not ConversationSummary summary) return;
        var position = e.GetPosition(ConversationListBox);
        if (Math.Abs(position.X - _conversationDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _conversationDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ConversationListBox, summary.Id, DragDropEffects.Move);
    }

    private async void ProjectListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat) ||
            e.Data.GetData(DataFormats.StringFormat) is not string conversationId) return;
        var target = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as WorkspaceProject;
        if (target is null) return;
        var document = await _workspace.FindAsync(conversationId);
        if (document is null || document.ProjectId == target.Id) return;
        _selectedConversation = await _workspace.MoveAsync(document, target.Id);
        _activeProjectId = target.Id;
        await RefreshWorkspaceAsync(_selectedConversation.Id);
        ShowNotice("会话 Markdown 已拖入项目文件夹。", NoticeKind.Success);
    }

    private async void Conversation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationListBox.SelectedItem is not ConversationSummary summary) return;
        _selectedConversation = await _workspace.FindAsync(summary.Id);
        DisplayConversation(_selectedConversation);
    }

    private async void RenameConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null) return;
        try
        {
            _selectedConversation = await _workspace.RenameAsync(_selectedConversation, ConversationTitleTextBox.Text);
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice("本地显示名称已更新；Copilot 网页标题仍被保留。", NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void MoveConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null || MoveProjectComboBox.SelectedValue is not string projectId) return;
        try
        {
            _selectedConversation = await _workspace.MoveAsync(_selectedConversation, projectId);
            _activeProjectId = projectId;
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice("会话 Markdown 已移动到所选项目文件夹。", NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private void SearchConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null) return;
        var query = ConversationSearchTextBox.Text.Trim();
        var results = _workspace.Search(_selectedConversation, query);
        SearchResultsText.Text = results.Count == 0
            ? (query.Length == 0 ? "请输入关键词。" : "未命中当前会话。")
            : string.Join(Environment.NewLine + Environment.NewLine, results.Select(result =>
                $"{result.Turn.Timestamp.LocalDateTime:MM-dd HH:mm} · {result.Turn.Role} · " +
                $"{result.Turn.Model ?? "—"}{Environment.NewLine}{result.Snippet}"));
    }

    private void CopyConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null) return;
        Clipboard.SetText(_workspace.Render(_selectedConversation));
        ShowNotice("当前会话 Markdown 已复制，可粘贴到 Codex 或其他工具。", NoticeKind.Success);
    }

    private async Task<ConversationDocument> CreateImmediateConversationAsync()
    {
        var conversation = await _workspace.CreateConversationAsync(_activeProjectId);
        conversation = conversation with
        {
            Mode = _settings.CollaborationMode.ToString().ToLowerInvariant(),
            CopilotConversationUrl = $"https://{_selectors.AllowedHost}/chat/"
        };
        await _workspace.SaveAsync(conversation);
        return conversation;
    }

    private async Task RefreshWorkspaceAsync(string? selectConversationId = null)
    {
        _projects = await _workspace.GetProjectsAsync();
        ProjectListBox.ItemsSource = _projects;
        MoveProjectComboBox.ItemsSource = _projects;
        if (_projects.All(project => project.Id != _activeProjectId)) _activeProjectId = ConversationWorkspaceStore.InboxProjectId;
        SelectProject(_activeProjectId);
        await RefreshConversationListAsync(selectConversationId);
    }

    private async Task RefreshConversationListAsync(string? selectConversationId = null)
    {
        var conversations = await _workspace.GetConversationsAsync(_activeProjectId);
        ConversationListBox.ItemsSource = conversations;
        var target = selectConversationId ?? _selectedConversation?.Id;
        var selected = conversations.FirstOrDefault(item => item.Id == target);
        if (selected is not null)
        {
            ConversationListBox.SelectedItem = selected;
            _selectedConversation = await _workspace.FindAsync(selected.Id);
        }
        else
        {
            _selectedConversation = null;
        }
        DisplayConversation(_selectedConversation);
    }

    private void SelectProject(string projectId)
    {
        ProjectListBox.SelectedItem = _projects.FirstOrDefault(project => project.Id == projectId);
    }

    private void DisplayConversation(ConversationDocument? document)
    {
        var visible = document is not null;
        ConversationEmptyText.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        ConversationDetailPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        ConversationTitleTextBox.Text = document!.DisplayTitle;
        CopilotTitleText.Text = document.CopilotTitleCurrent;
        CopilotTitleHistoryText.Text = document.CopilotTitleHistory.Count == 0
            ? document.CopilotTitleInitial
            : string.Join(" → ", document.CopilotTitleHistory);
        ConversationMetaText.Text = $"{document.Mode} · {document.Turns.Count} 条记录 · {document.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
        ConversationMarkdownTextBox.Text = _workspace.Render(document);
        MoveProjectComboBox.SelectedValue = document.ProjectId;
        ConversationSearchTextBox.Text = string.Empty;
        SearchResultsText.Text = "";
    }

    private async void StatusRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _statusRefreshTimer.Stop();
        await RefreshStatusAsync(automatic: true);
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        _windowIsActive = true;
        ScheduleStatusRefresh();
        if (_activePage == "overview" && !_busy)
        {
            _ = RefreshStatusAsync(automatic: true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _windowIsActive = false;
        ScheduleStatusRefresh();
    }

    private void Window_StateChanged(object? sender, EventArgs e) => ScheduleStatusRefresh();

    private async Task RefreshStatusAsync(bool automatic = false)
    {
        if (_busy)
        {
            if (automatic) ScheduleStatusRefresh();
            return;
        }
        SetBusy(true, _session is null ? "等待 Edge 授权" : "正在刷新状态");
        try
        {
            var session = await GetSessionAsync();
            await ReadConnectedStatusAsync(session);
            _consecutiveStatusRefreshFailures = 0;
            _lastStatusRefresh = DateTimeOffset.Now;
            if (!automatic) ClearNotice();
        }
        catch (Exception exception)
        {
            SetDisconnectedState();
            _consecutiveStatusRefreshFailures++;
            if (!automatic) ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            SetBusy(false, EdgeStatusText.Text == "已连接" ? "Edge 已连接" : "需要设置");
            ScheduleStatusRefresh();
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
        if (_session is not null && !_session.Page.IsClosed) return _session;
        await ResetSessionAsync();
        _session = await EdgeSessionAdapter.ConnectAsync(_settings, _selectors, timeoutMilliseconds: 30_000);
        return _session;
    }

    private async Task ResetSessionAsync()
    {
        if (_session is null) return;
        await _session.DisposeAsync();
        _session = null;
    }

    protected override async void OnClosed(EventArgs e)
    {
        _statusRefreshTimer.Stop();
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
        var workspaceDirectory = WorkspaceDirectoryTextBox.Text.Trim();
        if (workspaceDirectory.Length == 0) throw new InvalidDataException("本地会话工作区不能为空。");
        _settings = _settings with
        {
            MenuMinimumWaitMilliseconds = menuMinimum,
            MenuMaximumWaitMilliseconds = menuMaximum,
            ReplyTimeoutSeconds = replyTimeout,
            ConsultationPolicy = (ConsultationPolicy)Math.Max(0, PolicyComboBox.SelectedIndex),
            CollaborationMode = ReviewRadio.IsChecked == true ? CollaborationMode.Review :
                OutsourceRadio.IsChecked == true ? CollaborationMode.Outsource : CollaborationMode.Assist,
            ConversationWorkspaceDirectory = workspaceDirectory
        };
        await _settingsStore.SaveAsync(_settings);
        _workspace = new ConversationWorkspaceStore(_settings.ConversationWorkspaceDirectory);
        await RefreshWorkspaceAsync();
    }

    private void ApplySettingsToControls()
    {
        PolicyComboBox.SelectedIndex = (int)_settings.ConsultationPolicy;
        AssistRadio.IsChecked = _settings.CollaborationMode == CollaborationMode.Assist;
        OutsourceRadio.IsChecked = _settings.CollaborationMode == CollaborationMode.Outsource;
        ReviewRadio.IsChecked = _settings.CollaborationMode == CollaborationMode.Review;
        MenuMinimumTextBox.Text = _settings.MenuMinimumWaitMilliseconds.ToString();
        MenuMaximumTextBox.Text = _settings.MenuMaximumWaitMilliseconds.ToString();
        ReplyTimeoutTextBox.Text = _settings.ReplyTimeoutSeconds.ToString();
        WorkspaceDirectoryTextBox.Text = _settings.ConversationWorkspaceDirectory;
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
        NewProjectButton.IsEnabled = !busy;
        NewConversationButton.IsEnabled = !busy;
    }

    private void ScheduleStatusRefresh()
    {
        if (!IsLoaded) return;
        var interval = StatusRefreshSchedule.NextInterval(
            _activePage == "overview",
            _windowIsActive,
            WindowState == WindowState.Minimized,
            _consecutiveStatusRefreshFailures);
        _statusRefreshTimer.Stop();
        _statusRefreshTimer.Interval = interval;
        _statusRefreshTimer.Start();

        AutoRefreshStatusText.Text = _consecutiveStatusRefreshFailures > 0
            ? $"自动刷新失败 {_consecutiveStatusRefreshFailures} 次；将在 {interval.TotalSeconds:0} 秒后重试。"
            : _lastStatusRefresh is null
                ? $"自动刷新已开启：概览前台每 10 秒，后台每 60 秒。"
                : $"自动刷新已开启 · 上次检查 {_lastStatusRefresh.Value.LocalDateTime:HH:mm:ss} · 下次约 {interval.TotalSeconds:0} 秒后。";
    }

    private void ShowNotice(string message, NoticeKind kind)
    {
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
        NoticeBorder.Background = Brush(kind switch { NoticeKind.Success => "#EAF7EF", NoticeKind.Error => "#FFF0F0", _ => "#FFF4E5" });
        NoticeBorder.BorderBrush = Brush(kind switch { NoticeKind.Success => "#B9E5C9", NoticeKind.Error => "#F3C0C0", _ => "#FFD7A3" });
        NoticeText.Foreground = Brush(kind switch { NoticeKind.Success => "#176B3A", NoticeKind.Error => "#9B2C2C", _ => "#7A4A0A" });
    }

    private void ClearNotice() => NoticeBorder.Visibility = Visibility.Collapsed;
    private static void SetNavState(Button button, bool selected) { button.Background = Brush(selected ? "#EAF3FF" : "#00FFFFFF"); button.Foreground = Brush(selected ? "#0A6CFF" : "#4D5868"); }
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private static T? FindParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
    private static string FriendlyMessage(Exception exception) => exception.Message switch
    {
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) => "Edge 远程调试尚未开启。请在 edge://inspect 的 Remote debugging 页面允许当前浏览器实例。",
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) => "没有发现可用的 Microsoft 365 Copilot 聊天标签页。请先打开 https://m365.cloud.microsoft/chat/。",
        var message when message.Contains("Found", StringComparison.OrdinalIgnoreCase) && message.Contains("eligible Copilot tabs", StringComparison.OrdinalIgnoreCase) => "发现多个 Copilot 聊天标签页。请只保留一个专用标签页后重试。",
        var message when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) && message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) => "等待 Edge 允许远程访问超时。请在 Edge 中选择“允许”，然后点击刷新状态；本次运行的后续操作会复用同一连接。",
        _ => exception.Message
    };

    private enum NoticeKind { Info, Success, Error }
}
