using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CopilotBridge.Core;

namespace CopilotBridge.UI;

internal static class UiText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Copilot Bridge"] = "Copilot Bridge",
        ["v1.2.1 界面与可访问性"] = "v1.2.1 UI & Accessibility",
        ["概览"] = "Overview",
        ["对话管理"] = "Conversation Management",
        ["协作默认设置"] = "Collaboration Defaults",
        ["浏览器与模型"] = "Browser & Models",
        ["设置"] = "Settings",
        ["连接状态"] = "Connection status",
        ["状态提示"] = "Status notification",
        ["项目列表"] = "Project list",
        ["模型优先级列表"] = "Model priority list",
        ["会话列表"] = "Conversation list",
        ["按 Alt+上移或 Alt+下移调整所选项目顺序"] = "Press Alt+Up or Alt+Down to reorder the selected project",
        ["按 Alt+上移或 Alt+下移调整所选模型顺序"] = "Press Alt+Up or Alt+Down to reorder the selected model",
        ["使用会话详情中的项目选择框和移动按钮进行键盘移动"] = "Use the project selector and Move button in conversation details for keyboard movement",
        ["数据保存在你的本地工作区"] = "Your data stays in your local workspace",
        ["正在初始化"] = "Initializing",
        ["检查后台连接，并将新产生的即时咨询完整写入本地 Markdown 会话。"] = "Check the background connection and save each new immediate consultation as a complete local Markdown conversation.",
        ["检查后台连接状态，并管理专用 Copilot 标签页绑定。"] = "Check the background connection and manage the dedicated Copilot tab binding.",
        ["Edge 连接"] = "Edge connection",
        ["尚未检查"] = "Not checked",
        ["专用标签页"] = "Dedicated tab",
        ["未绑定"] = "Not bound",
        ["Microsoft 365 登录"] = "Microsoft 365 sign-in",
        ["未知"] = "Unknown",
        ["页面当前模型"] = "Current page model",
        ["刷新状态"] = "Refresh status",
        ["绑定当前 Copilot 标签页"] = "Bind current Copilot tab",
        ["将 Bridge 绑定到当前已登录的专用 Copilot 标签页。"] = "Bind Bridge to the current signed-in dedicated Copilot tab.",
        ["关闭提示"] = "Close notification",
        ["自动刷新正在准备。"] = "Automatic refresh is preparing.",
        ["即时咨询"] = "Immediate consultation",
        ["发送后会保存发送内容、Copilot 回复和已验证的实际模型；不会自动导入旧网页历史。"] = "After sending, the prompt, Copilot reply, and verified model are saved; existing web history is never imported automatically.",
        ["这是 Copilot Bridge v1.1 即时会话测试。请只回复：COPILOT_BRIDGE_V11_OK"] = "This is a Copilot Bridge v1.1 immediate-conversation test. Reply only: COPILOT_BRIDGE_V11_OK",
        ["发送并记录即时咨询"] = "Send and record consultation",
        ["集中管理本地 Markdown 对话，并可显式导入当前打开的旧网页对话。"] = "Manage local Markdown conversations and explicitly import the currently open web conversation.",
        ["导入对话"] = "Import conversation",
        ["项目"] = "Projects",
        ["项目文件夹"] = "Project folders",
        ["拖动项目卡片排序；未分类对话的位置已锁定。"] = "Drag project cards to reorder; the Unclassified conversations project is locked in place.",
        ["项目名称"] = "Project name",
        ["输入项目名称"] = "Enter a project name",
        ["创建项目"] = "Create project",
        ["Agent 访问权限"] = "Agent access",
        ["关闭（默认）"] = "Off (default)",
        ["仅元数据"] = "Metadata only",
        ["检索片段"] = "Search snippets",
        ["完整会话"] = "Full conversations",
        ["保存访问权限"] = "Save access",
        ["只控制本地 MCP 读取，不代表允许向 Copilot 发送。"] = "Controls local MCP reads only; it does not authorize sending to Copilot.",
        ["项目的 Agent 访问权限已保存。"] = "The project's Agent access has been saved.",
        ["可将会话拖到项目中归类"] = "Drag conversations into projects to categorize them",
        ["拖动项目卡片排序，也可将会话拖入项目归类"] = "Drag project cards to reorder, or drag conversations into a project",
        ["拖动排序"] = "Drag to reorder",
        ["位置已锁定"] = "Position locked",
        ["会话（可拖入项目）"] = "Conversations (drag into a project)",
        ["选择或创建一个即时会话"] = "Select or create an immediate conversation",
        ["本地显示名称"] = "Local display name",
        ["保存名称"] = "Save name",
        ["移动到："] = "Move to:",
        ["移动"] = "Move",
        ["复制 Markdown"] = "Copy Markdown",
        ["会话内检索"] = "Search this conversation",
        ["检索"] = "Search",
        ["Markdown 对话"] = "Markdown conversation",
        ["这里定义新会话的默认行为；即时会话记录会保存实际使用的模式和模型。"] = "Define default behavior for new conversations; immediate records retain the actual mode and model used.",
        ["征询策略"] = "Consultation policy",
        ["为新会话选择 Copilot 的介入方式。"] = "Choose how Copilot participates in new conversations.",
        ["关闭"] = "Disabled",
        ["仅手动（默认）"] = "Manual only (default)",
        ["Codex 可自动征询"] = "Codex may consult automatically",
        ["关键设计必须征询"] = "Required for key designs",
        ["默认协作模式"] = "Default collaboration mode",
        ["Assist — 快速聚焦的第二意见"] = "Assist — focused second opinion",
        ["Outsource — 开放式推理"] = "Outsource — open-ended reasoning",
        ["Review — 两个隔离 reviewer"] = "Review — two isolated reviewers",
        ["保存协作默认设置"] = "Save collaboration defaults",
        ["浏览器、模型与本地工作区"] = "Browser, models & local workspace",
        ["模型队列是默认回退策略；会话正文仅写入你选择的本地工作区。"] = "The model queue is the default fallback strategy; conversation content is written only to your chosen local workspace.",
        ["浏览器与模型"] = "Browser & models",
        ["配置模型回退顺序、菜单等待与回复超时。"] = "Configure model fallback order, menu wait, and reply timeout.",
        ["标签页绑定"] = "Tab binding",
        ["当前绑定"] = "Currently bound",
        ["重新绑定"] = "Rebind",
        ["本地会话工作区"] = "Local conversation workspace",
        ["即时会话以一会话一 Markdown 文件保存在此处；旧网页历史不会自动导入。"] = "Immediate conversations are stored here as one Markdown file each; existing web history is never imported automatically.",
        ["打开"] = "Open",
        ["浏览"] = "Browse",
        ["模型优先级"] = "Model priority",
        ["Opus → GPT 5.6 Think deeper → 深度思考。仅在高优先级不可用时回退。"] = "Opus → GPT 5.6 Think deeper → Deep thinking. Fallback occurs only when a higher-priority model is unavailable.",
        ["拖动选项卡排序；仅在上方模型不可用时回退。"] = "Drag cards to reorder. Fallback occurs only when a higher model is unavailable.",
        ["等待设置"] = "Wait settings",
        ["沟通轮次不设上限。"] = "Conversation turns are unlimited.",
        ["菜单最短等待（ms）"] = "Minimum menu wait (ms)",
        ["菜单最大等待（ms）"] = "Maximum menu wait (ms)",
        ["回复超时（秒）"] = "Reply timeout (seconds)",
        ["保存浏览器与工作区设置"] = "Save browser and workspace settings",
        ["保存浏览器与模型设置"] = "Save browser and model settings",
        ["显示语言"] = "Display language",
        ["外观与语言"] = "Appearance & language",
        ["主题"] = "Theme",
        ["后台常驻"] = "Background resident",
        ["关闭 GUI 后保持 MCP 后台进程"] = "Keep the MCP background process after closing the GUI",
        ["开启时，关闭 GUI 会询问是否终止 MCP；关闭时，关闭 GUI 会直接终止 Bridge 自己登记的 MCP 进程。"] = "When enabled, closing the GUI asks whether to stop MCP. When disabled, closing the GUI stops only MCP processes registered by this Bridge.",
        ["MCP 后台进程仍在运行。是否终止并关闭 GUI？"] = "The MCP background process is still running. Stop it and close the GUI?",
        ["浅色（默认）"] = "Light (default)",
        ["深色（Copilot）"] = "Dark (Copilot)",
        ["深色主题采用 Microsoft Copilot 风格的低眩光中性画布。"] = "Dark theme uses a low-glare neutral canvas inspired by Microsoft Copilot.",
        ["快捷方式"] = "Shortcuts",
        ["选择 Copilot Bridge 的启动位置，并管理其启动方式。"] = "Choose where Copilot Bridge is launched from and manage how it starts.",
        ["固定"] = "Pin",
        ["固定到任务栏"] = "Pin to taskbar",
        ["固定到“开始”"] = "Pin to Start",
        ["创建桌面快捷方式"] = "Create desktop shortcut",
        ["Copilot Bridge 已固定到任务栏。"] = "Copilot Bridge was pinned to the taskbar.",
        ["Windows 未开放自动固定；已选中快捷方式，请右键选择“固定到任务栏”。"] = "Windows did not expose automatic pinning. The shortcut is selected; right-click it and choose Pin to taskbar.",
        ["Copilot Bridge 已固定到“开始”。"] = "Copilot Bridge was pinned to Start.",
        ["已创建“开始”菜单快捷方式；如需固定磁贴，请右键选择“固定到‘开始’”。"] = "The Start menu shortcut was created. To pin it, right-click and choose Pin to Start.",
        ["桌面快捷方式已创建。"] = "The desktop shortcut was created.",
        ["立即切换应用界面语言；该偏好会保存在本机设置中。"] = "Switch the application interface language immediately; this preference is saved in local settings.",
        ["中文"] = "Chinese",
        ["保存设置"] = "Save settings",
        ["就绪"] = "Ready",
        ["设置已保存"] = "Settings saved",
        ["项目顺序已更新。"] = "Project order updated.",
        ["模型优先级已调整；点击保存浏览器与模型设置以持久化。"] = "Model priority updated; choose Save browser and model settings to persist it.",
        ["需要设置"] = "Setup required",
        ["等待 Edge 授权"] = "Waiting for Edge permission",
        ["正在绑定标签页"] = "Binding tab",
        ["正在咨询 Copilot"] = "Consulting Copilot",
        ["正在读取旧对话预览"] = "Reading conversation import preview",
        ["正在刷新状态"] = "Refreshing status",
        ["状态刷新正在完成，请稍后再试。"] = "Status refresh is finishing. Try again in a moment.",
        ["已连接"] = "Connected",
        ["已绑定专用 Copilot 标签页"] = "Dedicated Copilot tab is bound",
        ["Edge 已连接"] = "Edge connected",
        ["已登录"] = "Signed in",
        ["未连接"] = "Disconnected",
        ["无法确认"] = "Cannot confirm",
        ["Review 使用两个隔离会话"] = "Review uses two isolated conversations",
        ["需要检查"] = "Needs attention",
        ["设置已保存，将从下一次咨询开始生效。"] = "Settings saved and will apply to the next consultation.",
        ["正在处理当前咨询；会话改名和移动会暂时不可用。"] = "The current consultation is running; renaming and moving this conversation are temporarily unavailable.",
        ["已绑定当前专用 Copilot 标签页。"] = "The current dedicated Copilot tab is bound.",
        ["请先输入即时咨询内容。"] = "Enter an immediate consultation first.",
        ["已有一个咨询正在执行，请等待其完成。"] = "A consultation is already running. Please wait for it to finish.",
        ["征询策略当前为“关闭”，请先在协作页调整。"] = "The consultation policy is disabled. Change it on the Collaboration page first.",
        ["请先绑定一个专用 Copilot 标签页。 "] = "Bind a dedicated Copilot tab first.",
        ["即时会话已保存为本地 Markdown；不会自动读取旧网页历史。"] = "The immediate conversation was saved as local Markdown; existing web history is not read automatically.",
        ["已创建即时会话。输入内容后在概览页发送，正文会写入该会话。"] = "An immediate conversation was created. Send from Overview to write its content here.",
        ["会话 Markdown 已拖入项目文件夹。"] = "Conversation Markdown was moved into the project folder.",
        ["本地显示名称已更新；Copilot 网页标题仍被保留。"] = "The local display name was updated; the Copilot web title is retained.",
        ["会话 Markdown 已移动到所选项目文件夹。"] = "Conversation Markdown was moved to the selected project folder.",
        ["请输入关键词。"] = "Enter a keyword.",
        ["未命中当前会话。"] = "No matches in this conversation.",
        ["当前会话 Markdown 已复制，可粘贴到 Codex 或其他工具。"] = "The current conversation Markdown was copied. You can paste it into Codex or another tool.",
        ["重命名"] = "Rename",
        ["删除"] = "Delete",
        ["置顶"] = "Pin to top",
        ["取消置顶"] = "Unpin",
        ["已置顶"] = "Pinned",
        ["重命名项目"] = "Rename project",
        ["删除项目"] = "Delete project",
        ["重命名会话"] = "Rename conversation",
        ["删除会话"] = "Delete conversation",
        ["保存"] = "Save",
        ["取消"] = "Cancel",
        ["请输入名称"] = "Enter a name",
        ["项目已重命名。"] = "Project renamed.",
        ["项目已置顶。"] = "Project pinned to top.",
        ["已取消项目置顶。"] = "Project unpinned.",
        ["仅可删除不含会话的项目。确定删除此项目吗？"] = "Only an empty project can be deleted. Delete this project?",
        ["项目已删除。"] = "Project deleted.",
        ["将永久删除此本地 Markdown 会话，Copilot 网页对话不会受影响。确定删除吗？"] = "This permanently deletes the local Markdown conversation. The Copilot web conversation is unaffected. Delete it?",
        ["会话已删除。"] = "Conversation deleted.",
        ["将只读取并保存当前页面已加载的消息，不会发送、滚动、导航或改写 Copilot 对话。"] = "Only messages currently loaded on this page will be read and saved. Copilot will not be sent to, scrolled, navigated, or modified.",
        ["Copilot 标题"] = "Copilot title",
        ["已加载消息"] = "Loaded messages",
        ["用户"] = "User",
        ["历史回复模型"] = "Historic reply model",
        ["未知，不推断"] = "Unknown; not inferred",
        ["确认导入旧对话"] = "Confirm conversation import",
        ["已取消导入，未写入任何本地文件。"] = "Import cancelled; no local file was written.",
        ["旧对话已保存为本地 Markdown；历史回复模型保持未知。"] = "The web conversation was saved as local Markdown; historic reply models remain unknown.",
        ["当前 Copilot 对话已经导入过，不会创建重复 Markdown。"] = "This Copilot conversation was already imported; no duplicate Markdown was created.",
        ["等待时间无效：最大等待必须大于或等于最短等待，回复超时必须大于 0。"] = "Invalid wait values: maximum wait must be at least the minimum wait, and reply timeout must be greater than 0.",
        ["设置数值无效：请检查等待时间和回复超时。"] = "Invalid values: check wait times and reply timeout.",
        ["本地会话工作区不能为空。"] = "The local conversation workspace cannot be empty.",
        ["本地会话工作区不存在：{0}"] = "The local conversation workspace does not exist: {0}",
        ["选择本地会话工作区"] = "Choose local conversation workspace",
        ["Edge 远程调试尚未开启。请在 edge://inspect 的 Remote debugging 页面允许当前浏览器实例。"] = "Edge remote debugging is not enabled. Allow this browser instance from edge://inspect's Remote debugging page.",
        ["没有发现可用的 Copilot 聊天标签页。请打开 https://m365.cloud.microsoft/chat/ 或 https://copilot.cloud.microsoft/chat/。"] = "No eligible Copilot chat tab was found. Open https://m365.cloud.microsoft/chat/ or https://copilot.cloud.microsoft/chat/.",
        ["发现多个 Copilot 聊天标签页。请只保留一个专用标签页后重试。"] = "Multiple Copilot chat tabs were found. Keep only one dedicated tab, then try again.",
        ["等待 Edge 允许远程访问超时。请在 Edge 中选择“允许”，然后点击刷新状态；本次运行的后续操作会复用同一连接。"] = "Timed out waiting for Edge remote-access permission. Choose Allow in Edge, then refresh status; later actions in this run reuse the same connection."
    };

    private static readonly IReadOnlyDictionary<string, string> Chinese = English.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    internal static string Get(string chinese, AppLanguage language) =>
        language == AppLanguage.English && English.TryGetValue(chinese, out var english) ? english : chinese;

    internal static void Apply(Window window, AppLanguage language)
    {
        window.Title = Translate(window.Title, language);
        ApplyElement(window, language);
    }

    private static void ApplyElement(DependencyObject parent, AppLanguage language)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            switch (child)
            {
                case TextBlock textBlock:
                    textBlock.Text = Translate(textBlock.Text, language);
                    break;
                case TextBox textBox when textBox.Name == "TestPromptTextBox":
                    textBox.Text = Translate(textBox.Text, language);
                    break;
                case Button button when button.Content is string content:
                    button.Content = Translate(content, language);
                    break;
                case MenuItem menuItem when menuItem.Header is string header:
                    menuItem.Header = Translate(header, language);
                    break;
                case ComboBoxItem item when item.Content is string content:
                    item.Content = Translate(content, language);
                    break;
                case ComboBox comboBox:
                    foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
                    {
                        if (item.Content is string itemContent) item.Content = Translate(itemContent, language);
                    }
                    break;
                case RadioButton radio when radio.Content is string content:
                    radio.Content = Translate(content, language);
                    break;
                case CheckBox checkBox when checkBox.Content is string content:
                    checkBox.Content = Translate(content, language);
                    break;
            }

            if (child is FrameworkElement element && element.ToolTip is string tooltip)
            {
                element.ToolTip = Translate(tooltip, language);
            }

            if (child is FrameworkElement automationElement &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(automationElement)))
            {
                AutomationProperties.SetName(
                    automationElement,
                    Translate(AutomationProperties.GetName(automationElement), language));
            }

            if (child is FrameworkElement helpElement &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(helpElement)))
            {
                AutomationProperties.SetHelpText(
                    helpElement,
                    Translate(AutomationProperties.GetHelpText(helpElement), language));
            }

            ApplyElement(child, language);
        }
    }

    private static string Translate(string value, AppLanguage language) => language == AppLanguage.English
        ? English.TryGetValue(value, out var english) ? english : value
        : Chinese.TryGetValue(value, out var chinese) ? chinese : value;
}
