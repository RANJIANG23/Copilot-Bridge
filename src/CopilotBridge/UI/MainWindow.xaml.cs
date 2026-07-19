using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotBridge.Browser;
using CopilotBridge.Core;
using Microsoft.Win32;

namespace CopilotBridge.UI;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ConsultationStateStore _stateStore = new();
    private readonly McpProcessRegistry _mcpProcessRegistry = new();
    private readonly ProviderSelectors _selectors = ProviderSelectors.Load();
    private readonly DispatcherTimer _statusRefreshTimer = new();
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ObservableCollection<string> _modelPriority = [];
    private BridgeSettings _settings = new();
    private ConversationWorkspaceStore _workspace = new();
    private IReadOnlyList<WorkspaceProject> _projects = [];
    private ConversationDocument? _selectedConversation;
    private string _activeProjectId = ConversationWorkspaceStore.InboxProjectId;
    private EdgeSessionAdapter? _session;
    private bool _busy;
    private Point _conversationDragStart;
    private Point _modelPriorityDragStart;
    private string _activePage = "overview";
    private bool _windowIsActive;
    private int _consecutiveStatusRefreshFailures;
    private DateTimeOffset? _lastStatusRefresh;
    private bool _settingsAreLoaded;

    public MainWindow()
    {
        InitializeComponent();
        _statusRefreshTimer.Tick += StatusRefreshTimer_Tick;
        _noticeTimer.Tick += NoticeTimer_Tick;
        Activated += Window_Activated;
        Deactivated += Window_Deactivated;
        StateChanged += Window_StateChanged;
        SizeChanged += Window_SizeChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _windowIsActive = IsActive;
            _settings = await _settingsStore.LoadAsync();
            _workspace = new ConversationWorkspaceStore(_settings.ConversationWorkspaceDirectory);
            ModelPriorityListBox.ItemsSource = _modelPriority;
            ApplyTheme();
            ApplySettingsToControls();
            _settingsAreLoaded = true;
            ApplyUiLanguage();
            UpdateHistoryColumns();
            await RefreshWorkspaceAsync();
            await RefreshStatusAsync();
            ScheduleStatusRefresh();
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
            HeaderStatusText.Text = T("需要检查");
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
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        SetNavState(OverviewNav, page == "overview");
        SetNavState(HistoryNav, page == "history");
        SetNavState(CollaborationNav, page == "collaboration");
        SetNavState(BrowserNav, page == "browser");
        SetNavState(SettingsNav, page == "settings");
        if (page == "history") await RefreshWorkspaceAsync();
        ScheduleStatusRefresh();
        if (page == "overview" && !_busy)
        {
            await RefreshStatusAsync(automatic: true);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private void BrowseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var initialDirectory = WorkspaceDirectoryTextBox.Text.Trim();
        var dialog = new OpenFolderDialog
        {
            Title = T("选择本地会话工作区"),
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            WorkspaceDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private async void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsAreLoaded) return;
        var language = LanguageComboBox.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Chinese;
        if (_settings.DisplayLanguage == language) return;

        try
        {
            _settings = _settings with { DisplayLanguage = language };
            await _settingsStore.SaveAsync(_settings);
            ApplyUiLanguage();
            ShowNotice(T("设置已保存，将从下一次咨询开始生效。"), NoticeKind.Success);
            HeaderStatusText.Text = T("设置已保存");
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
    }

    private async void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsAreLoaded) return;
        var theme = ThemeComboBox.SelectedIndex == (int)AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        if (_settings.Theme == theme) return;

        var previous = _settings;
        try
        {
            _settings = _settings with { Theme = theme };
            await _settingsStore.SaveAsync(_settings);
            ApplyTheme();
            ApplyUiLanguage();
            ShowNotice(T("设置已保存，将从下一次咨询开始生效。"), NoticeKind.Success);
            HeaderStatusText.Text = T("设置已保存");
        }
        catch (Exception exception)
        {
            _settings = previous;
            ThemeComboBox.SelectedIndex = (int)previous.Theme;
            ApplyTheme();
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
    }

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
            TabStatusText.Text = T("已绑定专用 Copilot 标签页");
            ShowNotice(T("已绑定当前专用 Copilot 标签页。"), NoticeKind.Success);
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
            ApplyTheme();
            ApplyUiLanguage();
            ShowNotice(T("设置已保存，将从下一次咨询开始生效。"), NoticeKind.Success);
            HeaderStatusText.Text = T("设置已保存");
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var prompt = TestPromptTextBox.Text.Trim();
        if (prompt.Length == 0)
        {
            ShowNotice(T("请先输入即时咨询内容。"), NoticeKind.Error);
            return;
        }

        using var lease = ConsultationLease.TryAcquire();
        if (lease is null)
        {
            ShowNotice(T("已有一个咨询正在执行，请等待其完成。"), NoticeKind.Error);
            return;
        }

        SetBusy(true, "正在咨询 Copilot");
        try
        {
            await SaveSettingsFromControlsAsync();
            if (_settings.ConsultationPolicy == ConsultationPolicy.Disabled)
            {
                throw new InvalidOperationException(T("征询策略当前为“关闭”，请先在协作页调整。"));
            }

            var conversation = _selectedConversation ?? await CreateImmediateConversationAsync();
            var primaryUrl = conversation.CopilotConversationUrl ?? _settings.BoundConversationUrl;
            if (_settings.CollaborationMode != CollaborationMode.Review && string.IsNullOrWhiteSpace(primaryUrl))
            {
                throw new InvalidOperationException(T("请先绑定一个专用 Copilot 标签页。 "));
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

            BoundUrlText.Text = _settings.BoundConversationUrl ?? T("Review 使用两个隔离会话");
            TabStatusText.Text = T("已绑定专用 Copilot 标签页");
            ModelStatusText.Text = last.Result.Model;
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice(T("即时会话已保存为本地 Markdown；不会自动读取旧网页历史。"), NoticeKind.Success);
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

    private async void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not WorkspaceProject project) return;
        _activeProjectId = project.Id;
        await RefreshConversationListAsync();
    }

    private void ProjectListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is WorkspaceProject project)
        {
            ProjectListBox.SelectedItem = project;
        }
    }

    private void ProjectListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var project = ProjectListBox.SelectedItem as WorkspaceProject;
        var canModify = project is { IsSystem: false };
        PinProjectMenuItem.IsEnabled = canModify;
        PinProjectMenuItem.Header = canModify && project!.IsPinned ? T("取消置顶") : T("置顶");
        RenameProjectMenuItem.IsEnabled = canModify;
        DeleteProjectMenuItem.IsEnabled = canModify;
    }

    private async void PinProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not WorkspaceProject project || project.IsSystem) return;
        try
        {
            var updated = await _workspace.SetProjectPinnedAsync(project, !project.IsPinned);
            _activeProjectId = updated.Id;
            await RefreshWorkspaceAsync();
            SelectProject(updated.Id);
            ShowNotice(T(updated.IsPinned ? "项目已置顶。" : "已取消项目置顶。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void RenameProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not WorkspaceProject project || project.IsSystem) return;
        var name = PromptForName(T("重命名项目"), project.Name);
        if (name is null) return;

        try
        {
            var renamed = await _workspace.RenameProjectAsync(project, name);
            _activeProjectId = renamed.Id;
            await RefreshWorkspaceAsync();
            SelectProject(renamed.Id);
            ShowNotice(T("项目已重命名。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void DeleteProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not WorkspaceProject project || project.IsSystem) return;
        var confirmation = MessageBox.Show(
            T("仅可删除不含会话的项目。确定删除此项目吗？"),
            T("删除项目"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            await _workspace.DeleteProjectAsync(project);
            _activeProjectId = ConversationWorkspaceStore.InboxProjectId;
            await RefreshWorkspaceAsync();
            ShowNotice(T("项目已删除。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private void ConversationListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _conversationDragStart = e.GetPosition(ConversationListBox);

    private void ConversationListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is ConversationSummary summary)
        {
            ConversationListBox.SelectedItem = summary;
        }
    }

    private void ConversationListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ConversationListBox.SelectedItem is not ConversationSummary summary) return;
        var position = e.GetPosition(ConversationListBox);
        if (Math.Abs(position.X - _conversationDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _conversationDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ConversationListBox, summary.Id, DragDropEffects.Move);
    }

    private void ModelPriorityListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _modelPriorityDragStart = e.GetPosition(ModelPriorityListBox);

    private void ModelPriorityListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ModelPriorityListBox.SelectedItem is not string model) return;
        var position = e.GetPosition(ModelPriorityListBox);
        if (Math.Abs(position.X - _modelPriorityDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _modelPriorityDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ModelPriorityListBox, model, DragDropEffects.Move);
    }

    private void ModelPriorityListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat) ||
            e.Data.GetData(DataFormats.StringFormat) is not string model) return;
        var sourceIndex = _modelPriority.IndexOf(model);
        if (sourceIndex < 0) return;
        var targetModel = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as string;
        var targetIndex = targetModel is null ? _modelPriority.Count : _modelPriority.IndexOf(targetModel);
        if (targetIndex < 0) targetIndex = _modelPriority.Count;
        if (sourceIndex < targetIndex) targetIndex--;
        if (sourceIndex == targetIndex) return;

        _modelPriority.RemoveAt(sourceIndex);
        _modelPriority.Insert(targetIndex, model);
        ModelPriorityListBox.SelectedItem = model;
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
        ShowNotice(T("会话 Markdown 已拖入项目文件夹。"), NoticeKind.Success);
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
            ShowNotice(T("本地显示名称已更新；Copilot 网页标题仍被保留。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void RenameConversationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var conversation = await GetSelectedConversationAsync();
        if (conversation is null) return;
        var title = PromptForName(T("重命名会话"), conversation.DisplayTitle);
        if (title is null) return;

        try
        {
            _selectedConversation = await _workspace.RenameAsync(conversation, title);
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice(T("本地显示名称已更新；Copilot 网页标题仍被保留。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private async void DeleteConversationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var conversation = await GetSelectedConversationAsync();
        if (conversation is null) return;
        var confirmation = MessageBox.Show(
            T("将永久删除此本地 Markdown 会话，Copilot 网页对话不会受影响。确定删除吗？"),
            T("删除会话"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            await _workspace.DeleteConversationAsync(conversation);
            _selectedConversation = null;
            await RefreshWorkspaceAsync();
            ShowNotice(T("会话已删除。"), NoticeKind.Success);
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
            ShowNotice(T("会话 Markdown 已移动到所选项目文件夹。"), NoticeKind.Success);
        }
        catch (Exception exception) { ShowNotice(FriendlyMessage(exception), NoticeKind.Error); }
    }

    private void SearchConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null) return;
        var query = ConversationSearchTextBox.Text.Trim();
        var results = _workspace.Search(_selectedConversation, query);
        SearchResultsText.Text = results.Count == 0
            ? (query.Length == 0 ? T("请输入关键词。") : T("未命中当前会话。"))
            : string.Join(Environment.NewLine + Environment.NewLine, results.Select(result =>
                $"{result.Turn.Timestamp.LocalDateTime:MM-dd HH:mm} · {result.Turn.Role} · " +
                $"{result.Turn.Model ?? "—"}{Environment.NewLine}{result.Snippet}"));
    }

    private void CopyConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation is null) return;
        Clipboard.SetText(_workspace.Render(_selectedConversation));
        ShowNotice(T("当前会话 Markdown 已复制，可粘贴到 Codex 或其他工具。"), NoticeKind.Success);
    }

    private async Task<ConversationDocument?> GetSelectedConversationAsync()
    {
        if (ConversationListBox.SelectedItem is not ConversationSummary summary) return null;
        if (_selectedConversation?.Id == summary.Id) return _selectedConversation;
        _selectedConversation = await _workspace.FindAsync(summary.Id);
        return _selectedConversation;
    }

    private string? PromptForName(string title, string initialValue)
    {
        var input = new TextBox { Text = initialValue, MinWidth = 300, Margin = new Thickness(0, 0, 0, 16) };
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var confirm = new Button { Content = T("保存"), IsDefault = true, MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button { Content = T("取消"), IsCancel = true, MinWidth = 76 };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = T("请输入名称"), Margin = new Thickness(0, 0, 0, 6) },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, confirm }
                }
            }
        };
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return dialog.ShowDialog() == true ? input.Text : null;
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
        ConversationMetaText.Text = _settings.DisplayLanguage == AppLanguage.English
            ? $"{document.Mode} · {document.Turns.Count} records · {document.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}"
            : $"{document.Mode} · {document.Turns.Count} 条记录 · {document.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
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

    private async void ImportConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "正在读取旧对话预览");
        try
        {
            var session = await GetSessionAsync();
            var snapshot = await new CopilotPageDriver(session.Page, _selectors, _settings)
                .ReadCurrentConversationAsync();
            var userCount = snapshot.Turns.Count(turn => turn.Role == "user");
            var copilotCount = snapshot.Turns.Count - userCount;
            var preview = T("将只读取并保存当前页面已加载的消息，不会发送、滚动、导航或改写 Copilot 对话。") +
                Environment.NewLine + Environment.NewLine +
                $"{T("Copilot 标题")}：{snapshot.CopilotTitle}" + Environment.NewLine +
                $"URL：{snapshot.ConversationUrl}" + Environment.NewLine +
                $"{T("已加载消息")}：{snapshot.Turns.Count}（{T("用户")} {userCount} / Copilot {copilotCount}）" + Environment.NewLine +
                $"{T("历史回复模型")}：{T("未知，不推断")}";
            if (MessageBox.Show(preview, T("确认导入旧对话"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                ShowNotice(T("已取消导入，未写入任何本地文件。"), NoticeKind.Info);
                return;
            }

            _selectedConversation = await _workspace.ImportHistoricalConversationAsync(_activeProjectId, snapshot);
            await RefreshWorkspaceAsync(_selectedConversation.Id);
            ShowNotice(T("旧对话已保存为本地 Markdown；历史回复模型保持未知。"), NoticeKind.Success);
        }
        catch (Exception exception)
        {
            var existing = exception.Message.Contains("already been imported", StringComparison.OrdinalIgnoreCase)
                ? T("当前 Copilot 对话已经导入过，不会创建重复 Markdown。")
                : FriendlyMessage(exception);
            ShowNotice(existing, NoticeKind.Error);
        }
        finally { SetBusy(false, "就绪"); }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _windowIsActive = false;
        ScheduleStatusRefresh();
    }

    private void Window_StateChanged(object? sender, EventArgs e) => ScheduleStatusRefresh();
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHistoryColumns();

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
            SetBusy(false, EdgeStatusText.Text == T("已连接") ? "Edge 已连接" : "需要设置");
            ScheduleStatusRefresh();
        }
    }

    private async Task ReadConnectedStatusAsync(EdgeSessionAdapter session)
    {
        var driver = new CopilotPageDriver(session.Page, _selectors, _settings);
        EdgeStatusText.Text = T("已连接");
        EdgeStatusDot.Fill = Brush("#31C76A");
        LoginStatusText.Text = T("已登录");
        ModelStatusText.Text = await driver.ReadCurrentModelAsync();
        TabStatusText.Text = T("已绑定专用 Copilot 标签页");
        HeaderStatusText.Text = T("Edge 已连接");
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
        _noticeTimer.Stop();
        base.OnClosed(e);
        await ResetSessionAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settingsAreLoaded)
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var registrations = _mcpProcessRegistry.GetLiveRegistrations(executablePath);
                if (registrations.Count > 0)
                {
                    var shouldTerminate = !_settings.KeepMcpRunningInBackground ||
                        MessageBox.Show(
                            T("MCP 后台进程仍在运行。是否终止并关闭 GUI？"),
                            T("后台常驻"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes;
                    if (shouldTerminate)
                    {
                        _mcpProcessRegistry.TerminateRegisteredProcesses(executablePath);
                    }
                }
            }
        }

        base.OnClosing(e);
    }

    private void SetDisconnectedState()
    {
        EdgeStatusText.Text = T("未连接");
        EdgeStatusDot.Fill = Brush("#E55757");
        LoginStatusText.Text = T("无法确认");
        ModelStatusText.Text = T("未知");
        HeaderStatusText.Text = T("需要设置");
    }

    private async Task SaveSettingsFromControlsAsync()
    {
        if (!int.TryParse(MenuMinimumTextBox.Text, out var menuMinimum) || menuMinimum < 0 ||
            !int.TryParse(MenuMaximumTextBox.Text, out var menuMaximum) || menuMaximum < menuMinimum ||
            !int.TryParse(ReplyTimeoutTextBox.Text, out var replyTimeout) || replyTimeout <= 0 ||
            !int.TryParse(ConversationTurnLimitTextBox.Text, out var turnLimit) || turnLimit is < 1 or > 20)
        {
            throw new InvalidDataException(T("设置数值无效：请检查等待时间、回复超时和沟通轮次上限。"));
        }
        var workspaceDirectory = WorkspaceDirectoryTextBox.Text.Trim();
        if (workspaceDirectory.Length == 0) throw new InvalidDataException(T("本地会话工作区不能为空。"));
        _settings = _settings with
        {
            MenuMinimumWaitMilliseconds = menuMinimum,
            MenuMaximumWaitMilliseconds = menuMaximum,
            ReplyTimeoutSeconds = replyTimeout,
            ConversationTurnLimit = turnLimit,
            ModelPriority = ModelPriorityOptions.Serialize(_modelPriority),
            ConsultationPolicy = (ConsultationPolicy)Math.Max(0, PolicyComboBox.SelectedIndex),
            CollaborationMode = ReviewRadio.IsChecked == true ? CollaborationMode.Review :
                OutsourceRadio.IsChecked == true ? CollaborationMode.Outsource : CollaborationMode.Assist,
            DisplayLanguage = LanguageComboBox.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Chinese,
            Theme = ThemeComboBox.SelectedIndex == (int)AppTheme.Dark ? AppTheme.Dark : AppTheme.Light,
            KeepMcpRunningInBackground = KeepMcpRunningCheckBox.IsChecked == true,
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
        ConversationTurnLimitTextBox.Text = _settings.ConversationTurnLimit.ToString();
        WorkspaceDirectoryTextBox.Text = _settings.ConversationWorkspaceDirectory;
        _modelPriority.Clear();
        foreach (var model in ModelPriorityOptions.Parse(_settings.ModelPriority)) _modelPriority.Add(model);
        BoundUrlText.Text = _settings.BoundConversationUrl ?? T("未绑定");
        LanguageComboBox.SelectedIndex = (int)_settings.DisplayLanguage;
        ThemeComboBox.SelectedIndex = (int)_settings.Theme;
        KeepMcpRunningCheckBox.IsChecked = _settings.KeepMcpRunningInBackground;
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyHintText.Text = busy ? T("正在处理当前咨询；会话改名和移动会暂时不可用。") : string.Empty;
        BusyHintText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatusText.Text = T(status);
        RefreshButton.IsEnabled = !busy;
        BindButton.IsEnabled = !busy;
        TestButton.IsEnabled = !busy;
        SaveCollaborationButton.IsEnabled = !busy;
        SaveBrowserButton.IsEnabled = !busy;
        SaveSettingsButton.IsEnabled = !busy;
        BrowseWorkspaceButton.IsEnabled = !busy;
        ModelPriorityListBox.IsEnabled = !busy;
        ConversationTurnLimitTextBox.IsEnabled = !busy;
        LanguageComboBox.IsEnabled = !busy;
        ThemeComboBox.IsEnabled = !busy;
        NewProjectButton.IsEnabled = !busy;
        ImportConversationButton.IsEnabled = !busy;
        RenameConversationButton.IsEnabled = !busy;
        ConversationTitleTextBox.IsEnabled = !busy;
        MoveProjectComboBox.IsEnabled = !busy;
        MoveConversationButton.IsEnabled = !busy;
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

        AutoRefreshStatusText.Text = _settings.DisplayLanguage == AppLanguage.English
            ? _consecutiveStatusRefreshFailures > 0
                ? $"Automatic refresh failed {_consecutiveStatusRefreshFailures} time(s); retrying in {interval.TotalSeconds:0} seconds."
                : _lastStatusRefresh is null
                    ? "Automatic refresh is on: every 10 seconds while Overview is active, otherwise every 60 seconds."
                    : $"Automatic refresh is on · last checked {_lastStatusRefresh.Value.LocalDateTime:HH:mm:ss} · next check in about {interval.TotalSeconds:0} seconds."
            : _consecutiveStatusRefreshFailures > 0
                ? $"自动刷新失败 {_consecutiveStatusRefreshFailures} 次；将在 {interval.TotalSeconds:0} 秒后重试。"
                : _lastStatusRefresh is null
                    ? "自动刷新已开启：概览前台每 10 秒，后台每 60 秒。"
                    : $"自动刷新已开启 · 上次检查 {_lastStatusRefresh.Value.LocalDateTime:HH:mm:ss} · 下次约 {interval.TotalSeconds:0} 秒后。";
    }

    private void ShowNotice(string message, NoticeKind kind)
    {
        _noticeTimer.Stop();
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
        NoticeBorder.Background = ThemeBrush(kind switch { NoticeKind.Success => "NoticeSuccessBackgroundBrush", NoticeKind.Error => "NoticeErrorBackgroundBrush", _ => "NoticeInfoBackgroundBrush" });
        NoticeBorder.BorderBrush = ThemeBrush(kind switch { NoticeKind.Success => "NoticeSuccessBorderBrush", NoticeKind.Error => "NoticeErrorBorderBrush", _ => "NoticeInfoBorderBrush" });
        NoticeText.Foreground = ThemeBrush(kind switch { NoticeKind.Success => "NoticeSuccessTextBrush", NoticeKind.Error => "NoticeErrorTextBrush", _ => "NoticeInfoTextBrush" });
        NoticeCloseButton.Foreground = NoticeText.Foreground;
        _noticeTimer.Start();
    }

    private void NoticeClose_Click(object sender, RoutedEventArgs e) => ClearNotice();
    private void NoticeTimer_Tick(object? sender, EventArgs e) => ClearNotice();
    private void ClearNotice()
    {
        _noticeTimer.Stop();
        NoticeBorder.Visibility = Visibility.Collapsed;
    }

    private void UpdateHistoryColumns()
    {
        if (HistoryProjectColumn is null || HistoryConversationColumn is null) return;
        var compact = ActualWidth < 1180;
        HistoryProjectColumn.Width = new GridLength(compact ? 170 : 220);
        HistoryConversationColumn.Width = new GridLength(compact ? 210 : 280);
    }
    private void SetNavState(Button button, bool selected)
    {
        button.Background = selected ? ThemeBrush("NavSelectedBrush") : Brushes.Transparent;
        button.Foreground = ThemeBrush(selected ? "NavSelectedTextBrush" : "NavTextBrush");
    }

    private void ApplyTheme()
    {
        var dark = _settings.Theme == AppTheme.Dark;
        SetThemeBrush("CanvasBrush", dark ? "#1E1E1E" : "#F5F6F8");
        SetThemeBrush("SurfaceBrush", dark ? "#252526" : "#FFFFFF");
        SetThemeBrush("TextPrimaryBrush", dark ? "#F3F4F6" : "#1D232F");
        SetThemeBrush("AccentBrush", "#0A6CFF");
        SetThemeBrush("AccentTextBrush", dark ? "#8AB4F8" : "#0A6CFF");
        SetThemeBrush("OnAccentBrush", "#FFFFFF");
        SetThemeBrush("SubtleTextBrush", dark ? "#B5BDCA" : "#667085");
        SetThemeBrush("MutedTextBrush", dark ? "#98A2B3" : "#667085");
        SetThemeBrush("LineBrush", dark ? "#3A3A3A" : "#E4E7EC");
        SetThemeBrush("ControlBorderBrush", dark ? "#4B5563" : "#D7DCE3");
        SetThemeBrush("NavTextBrush", dark ? "#C9D1D9" : "#4D5868");
        SetThemeBrush("NavHoverBrush", dark ? "#30343B" : "#EDF0F4");
        SetThemeBrush("NavSelectedBrush", dark ? "#233B5A" : "#EAF3FF");
        SetThemeBrush("NavSelectedTextBrush", dark ? "#8AB4F8" : "#0A6CFF");
        SetThemeBrush("SecondaryActionBrush", dark ? "#E5E7EB" : "#344054");
        SetThemeBrush("StatusUnknownBrush", dark ? "#6B7280" : "#A8B0BC");
        SetThemeBrush("NoticeInfoBackgroundBrush", dark ? "#3B2F1B" : "#FFF4E5");
        SetThemeBrush("NoticeInfoBorderBrush", dark ? "#765E2A" : "#FFD7A3");
        SetThemeBrush("NoticeInfoTextBrush", dark ? "#F6C66B" : "#7A4A0A");
        SetThemeBrush("NoticeSuccessBackgroundBrush", dark ? "#173524" : "#EAF7EF");
        SetThemeBrush("NoticeSuccessBorderBrush", dark ? "#2B6A40" : "#B9E5C9");
        SetThemeBrush("NoticeSuccessTextBrush", dark ? "#9BE0AE" : "#176B3A");
        SetThemeBrush("NoticeErrorBackgroundBrush", dark ? "#3C2026" : "#FFF0F0");
        SetThemeBrush("NoticeErrorBorderBrush", dark ? "#7A3440" : "#F3C0C0");
        SetThemeBrush("NoticeErrorTextBrush", dark ? "#FFB4AB" : "#9B2C2C");
        SetSystemBrush(SystemColors.WindowBrushKey, dark ? "#252526" : "#FFFFFF");
        SetSystemBrush(SystemColors.WindowTextBrushKey, dark ? "#F3F4F6" : "#1D232F");
        SetSystemBrush(SystemColors.ControlBrushKey, dark ? "#252526" : "#FFFFFF");
        SetSystemBrush(SystemColors.ControlTextBrushKey, dark ? "#F3F4F6" : "#1D232F");
        SetSystemBrush(SystemColors.MenuBrushKey, dark ? "#252526" : "#FFFFFF");
        SetSystemBrush(SystemColors.MenuTextBrushKey, dark ? "#F3F4F6" : "#1D232F");
        SetNavState(OverviewNav, _activePage == "overview");
        SetNavState(HistoryNav, _activePage == "history");
        SetNavState(CollaborationNav, _activePage == "collaboration");
        SetNavState(BrowserNav, _activePage == "browser");
        SetNavState(SettingsNav, _activePage == "settings");
    }

    private SolidColorBrush ThemeBrush(string key) => (SolidColorBrush)Resources[key];
    private void SetThemeBrush(string key, string color)
    {
        var brush = Brush(color);
        Resources[key] = brush;
        Application.Current.Resources[key] = brush;
    }

    private static void SetSystemBrush(object key, string color) => Application.Current.Resources[key] = Brush(color);
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
    private void ApplyUiLanguage()
    {
        UiText.Apply(this, _settings.DisplayLanguage);
        PinProjectMenuItem.Header = T("置顶");
        RenameProjectMenuItem.Header = T("重命名");
        DeleteProjectMenuItem.Header = T("删除");
        RenameConversationMenuItem.Header = T("重命名");
        DeleteConversationMenuItem.Header = T("删除");
        ScheduleStatusRefresh();
    }

    private string T(string chinese) => UiText.Get(chinese, _settings.DisplayLanguage);

    private string FriendlyMessage(Exception exception) => exception.Message switch
    {
        var message when message.Contains("DevToolsActivePort", StringComparison.OrdinalIgnoreCase) => T("Edge 远程调试尚未开启。请在 edge://inspect 的 Remote debugging 页面允许当前浏览器实例。"),
        var message when message.Contains("No eligible", StringComparison.OrdinalIgnoreCase) => T("没有发现可用的 Microsoft 365 Copilot 聊天标签页。请先打开 https://m365.cloud.microsoft/chat/。"),
        var message when message.Contains("Found", StringComparison.OrdinalIgnoreCase) && message.Contains("eligible Copilot tabs", StringComparison.OrdinalIgnoreCase) => T("发现多个 Copilot 聊天标签页。请只保留一个专用标签页后重试。"),
        var message when message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) && message.Contains("ws connecting", StringComparison.OrdinalIgnoreCase) => T("等待 Edge 允许远程访问超时。请在 Edge 中选择“允许”，然后点击刷新状态；本次运行的后续操作会复用同一连接。"),
        _ => exception.Message
    };

    private enum NoticeKind { Info, Success, Error }
}
