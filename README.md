# Copilot Bridge

Copilot Bridge 是一个面向 Windows 的本地协作桥接工具。它让 Codex 通过本机 STDIO MCP、当前用户已登录的 Microsoft Edge，与已绑定的 Microsoft 365 Copilot 专用后台标签页进行受控协作。根据用户在 GUI 中选择的协作模式，Copilot 可以承担前置思考、独立审核、与 Codex 相对独立的并行推理，或对明确问题提供聚焦协助；Codex 始终负责基于证据的最终裁决与实际执行。

Copilot Bridge is a local Windows collaboration bridge. It lets Codex work through local STDIO MCP and a dedicated, bound Microsoft 365 Copilot background tab in the user's signed-in Microsoft Edge session. Depending on the collaboration mode selected in the GUI, Copilot can provide upfront reasoning, independent review, a reasoning branch independent from Codex, or focused assistance; Codex remains responsible for evidence-based final judgment and execution.

```text
Codex → STDIO MCP → Copilot Bridge → Edge CDP/DOM → Microsoft 365 Copilot
```

每次协作由三个彼此独立的控制面约束：**征询策略**决定何时允许咨询；**协作模式**决定 Assist、Outsource、Review 三种角色分工，且只能由用户在 GUI 中手动选择；**模型策略**决定允许使用的模型及优先级，并排除“自动”和快速响应类模型。“并行推理”指推理视角独立，不代表浏览器操作并发；实际 Copilot 会话始终在单一专用标签页中串行执行。

Every collaboration is governed by three independent control planes: **consultation policy** determines when a consultation is allowed; **collaboration mode** determines the Assist, Outsource, or Review division of work and can only be selected manually in the GUI; and **model policy** determines the allowed models and their priority while excluding Auto and quick-response models. “Parallel reasoning” means independent reasoning perspectives, not concurrent browser operations; Copilot sessions remain serial in one dedicated tab.

日常咨询不会模拟鼠标键盘、抢占前台窗口或切换用户正在使用的 Edge 标签页。Copilot 不执行本机操作，也不构成授权依据。

Routine consultations do not simulate physical input, take foreground focus, or switch the user's active Edge tab. Copilot does not execute local actions and is not an authorization source.

## 快速开始 / Quick start

### 下载 / Download

从 [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases/tag/v1.0.0-rc.5) 下载以下两个文件。Download both files from [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases/tag/v1.0.0-rc.5):

- `CopilotBridge-1.0.0-rc.5-win-x64.zip`
- `CopilotBridge-1.0.0-rc.5-win-x64.zip.sha256`

当前 ZIP SHA-256 / Current ZIP SHA-256:

```text
b31bb22178356fd94b707db77f29f8f00bee5aa450d3c751e4f791e6b9714a23
```

安装前可在 PowerShell 中核对。Verify it in PowerShell before installation:

```powershell
(Get-FileHash .\CopilotBridge-1.0.0-rc.5-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

### 使用前提 / Requirements

- Windows 11 x64
- Microsoft Edge 已登录成员自己的 Microsoft 365 企业账号 / Microsoft Edge signed in with the team member's own Microsoft 365 work account
- 该账号可以使用 Microsoft 365 Copilot / An account entitled to use Microsoft 365 Copilot
- Edge 从桌面或开始菜单正常启动默认配置档，并在 `edge://inspect` 中启用 Remote debugging，显示 `127.0.0.1:9222` / Edge launched normally from the desktop or Start menu with the default profile, with Remote debugging enabled at `edge://inspect` and showing `127.0.0.1:9222`
- Codex 桌面版或 Codex CLI / Codex desktop app or Codex CLI

Bridge 不保存或迁移账号、Cookie、令牌和 Microsoft 365 凭据。Bridge does not store or migrate accounts, cookies, tokens, or Microsoft 365 credentials.

### 安装并开始协作 / Install and collaborate

1. 完整解压 ZIP，不要只复制 EXE。Extract the complete ZIP; do not copy the EXE by itself.
2. 在解压目录运行以下命令。Run the following command from the extracted directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1
```

3. 从开始菜单打开 Copilot Bridge。Open Copilot Bridge from the Start menu.
4. 确认 Edge 与 Microsoft 365 Copilot 状态正常，然后绑定专用 Copilot 标签页。Confirm the Edge and Microsoft 365 Copilot status, then bind the dedicated Copilot tab.
5. 首次建议使用“仅手动 + Assist”。For the first run, use `Manual only + Assist`.
6. 保存设置，关闭 GUI，并新建一个 Codex 任务。Save the settings, close the GUI, and start a new Codex task.
7. 要求 Codex 使用 `copilot-consult` 对一个具体方案进行二次核验。Ask Codex to use `copilot-consult` for a focused second opinion.

不要为 Bridge 使用带远程调试参数的命令行方式启动 Edge；实测该方式可能停在 `Starting`。Do not launch Edge for Bridge with remote-debugging command-line flags; testing found that this path can remain stuck at `Starting`.

完整步骤见 [安装说明](./INSTALL.md)。发布者请同时执行 [G8 本机隔离验收](./TEAM-ROLLOUT.md#g8-本机隔离验收)。

See [Installation](./INSTALL.md) for the complete procedure. Release owners must also complete the [local isolated G8 acceptance](./TEAM-ROLLOUT.md#g8-本机隔离验收).

## v1.0 已完成内容 / v1.0 delivered

v1.0 建立了从 Codex 到 Microsoft 365 Copilot 的完整、可验证闭环：用户绑定自己的专用 Edge 标签页，Bridge 在后台完成受控咨询，Codex 根据结果继续完成任务。

v1.0 established a complete, verifiable path from Codex to Microsoft 365 Copilot: the user binds a dedicated Edge tab, Bridge performs a controlled background consultation, and Codex continues the task from the result.

- **受控后台会话**：只操作用户明确绑定的 Copilot 标签页，连接、模型选择、发送和回复提取均通过 Edge CDP 与 DOM 完成。**Controlled background sessions:** only the user-bound Copilot tab is operated, with connection, model selection, submission, and reply extraction performed through Edge CDP and DOM.
- **三种协作模式**：Assist 提供聚焦协助；Outsource 承担有限回合的开放式推理；Review 使用两个隔离会话审查复杂度、风险与证据。**Three collaboration modes:** Assist provides focused help; Outsource performs bounded open-ended reasoning; Review uses two isolated sessions to examine complexity, risk, and evidence.
- **最小 MCP 接口**：只提供状态读取与咨询两个工具，协作模式和模型策略由 GUI 控制，调用方不能临时覆盖。**Minimal MCP surface:** only status and consultation tools are exposed; the GUI controls collaboration mode and model policy.
- **一次性发送保护**：发送状态不确定时绝不自动重发；GUI 与 MCP 并发写入会立即返回 busy，而非排队。**One-time submission protection:** uncertain submissions are never retried; concurrent GUI and MCP writes return busy instead of queuing.
- **本机团队门禁**：Phase 0–6 与 G1–G8 已完成；真实日常 Edge 后台 Assist、十次唯一发送、MCP 接入、本机隔离安装/卸载与前台无抢占均已验证。**Local team gates:** Phase 0–6 and G1–G8 are complete, including real daily-Edge background Assist, ten unique submissions, MCP integration, isolated local install/uninstall, and no foreground takeover.

当前可下载的稳定发布包是 RC5。The currently downloadable stable release package is RC5.

## v1.1 会话工作台 / v1.1 conversation workspace

v1.1 把 Copilot Bridge 从“瞬时征询工具”扩展为受控的本地会话工作台：用户和获授权的本地 Agent 可以按会话、项目和选定范围复用 Microsoft 365 Copilot 的 Markdown 上下文，同时继续使用用户自己的已登录 Edge，不采用反向代理、不绕过 Microsoft 验证，也不自动读取未授权的网页历史。

v1.1 evolves Copilot Bridge from a transient consultation tool into a controlled local conversation workspace. Users and authorized local agents can reuse Microsoft 365 Copilot context by conversation, project, and explicitly selected scope while continuing to use the user's signed-in Edge session—without reverse proxying, bypassing Microsoft authentication, or silently importing existing web history.

### 已完成：Phase 7–10 / Delivered

- 新即时咨询完整保存为一会话一份 Markdown，包括实际发送内容、Copilot 回复、角色、时间和已验证模型。New immediate consultations are stored as one Markdown file per conversation, including the actual request, Copilot response, role, timestamp, and verified model.
- “历史对话”升级为项目、会话列表和详情三栏工作台；支持创建项目、移动、改名、会话内关键词检索和复制 Markdown。The History area is now a three-pane workspace for projects, conversations, and conversation details, with project creation, moving, renaming, in-conversation search, and Markdown copy.
- 会话使用稳定 ID 与 Copilot URL 关联；本地名称不会覆盖 Copilot 标题字段。Conversation identity is based on a stable ID and Copilot URL; local renaming does not overwrite Copilot title fields.
- 默认工作区为 `%LOCALAPPDATA%\CopilotBridge\workspace`，也可以在 GUI 中改为用户指定目录。The default workspace is `%LOCALAPPDATA%\CopilotBridge\workspace`, and users can choose another local directory in the GUI.
- 概览状态采用自适应刷新：概览前台每 10 秒、后台或其他页面每 60 秒；失败时按 30/60/120 秒退避。手动刷新仍可立即检查。Overview status now refreshes adaptively: every 10 seconds while the Overview is active, every 60 seconds in the background or on other pages, with 30/60/120-second retry backoff after failures.
- GUI 支持中文和 English，并将语言选择写入本地设置。The GUI supports Chinese and English, with the language choice persisted locally.
- 用户可以从“历史对话”显式导入当前唯一打开的旧 Copilot 对话：先查看标题、URL 和已加载消息数，确认后才保存为一份 Markdown。过程不会发送、滚动、导航或批量读取其他网页历史。Users can explicitly import the one currently open Copilot conversation from Conversation History: title, URL, and loaded-message count are previewed before a single Markdown file is saved. The flow does not send, scroll, navigate, or bulk-read other web history.
- 导入的 Copilot 回复逐条标注为 `unknown` 模型状态，不会用当前页面模型反推历史模型；同一 Copilot URL 不会重复创建 Markdown。Imported Copilot replies are marked `unknown` per turn rather than inferred from the current page model; the same Copilot URL cannot create duplicate Markdown.

### 后续目标 / Next milestones

- **双标题同步**：保留 Copilot 初始标题、当前标题和标题历史；本地重命名始终独立。**Dual-title sync:** preserve the initial, current, and historical Copilot titles while keeping the local display name independent.
- **会话级模型控制**：允许在受控白名单内为新会话或下一次请求选择模型，并明确显示实际模型与回退。**Conversation-level model control:** choose an allowed model for a new conversation or next request, with the effective model and fallback shown explicitly.
- **项目级受控调用**：本地 Agent 可以读取项目概览、项目内检索结果或用户确认的完整范围，不默认获得整个工作区。**Controlled project access:** local agents can read a project overview, search results, or a user-confirmed full scope without receiving the entire workspace by default.
- **内容交接**：支持将选中消息、当前会话或项目检索结果复制或插入当前 Codex 任务。**Context handoff:** copy or insert selected messages, the current conversation, or project search results into the active Codex task.

## 当前状态与限制 / Current status and limits

| 项目 / Item | 状态 / Status |
|---|---|
| 当前源码版本 / Current source version | `1.1.1` |
| 发布状态 / Release status | 团队 v1 已通过；v1.1.1 源码已验证，安装包尚未发布 / Team v1 passed; v1.1.1 source verified, installer package not yet published |
| 已通过 / Passed | Phase 0–6 and G1–G8（本机隔离验收 / local isolated acceptance） |
| 后续试点 / Follow-up pilot | 不同硬件、账号和企业策略环境 / Different hardware, account, and enterprise-policy environments |
| 平台 / Platform | Windows 11 x64 |

RC5 已达到项目定义的本机团队 v1 门禁，但不把本机隔离验收描述为跨设备兼容性证明。`1.1.1` 尚未发布安装包；现有 RC5 下载仍是最后一个发布包。

RC5 satisfies the project's local team-v1 gates, but local isolated acceptance is not presented as proof of cross-device compatibility. `1.1.1` has not been packaged for release.

## 架构开发思路 / Architecture and design rationale

Bridge 的设计优先保证用户控制、可验证性与最小运行边界，而不是构建通用 Agent 或浏览器自动化平台。

Bridge prioritizes user control, verifiability, and a minimal runtime boundary rather than becoming a general agent or browser-automation platform.

- **单一运行单元**：只有一个生产项目、一个生产可执行文件 `CopilotBridge.exe`；GUI 与 STDIO MCP 共享业务代码。**One production unit:** one production project and one executable, `CopilotBridge.exe`, with shared GUI and STDIO MCP business logic.
- **浏览器边界**：只经 Edge CDP 与绑定标签页 DOM 操作，不使用 Computer Use、OCR、Windows UI Automation、物理鼠标键盘模拟或前台窗口切换。**Browser boundary:** only Edge CDP and the bound-tab DOM are used; no Computer Use, OCR, Windows UI Automation, physical input simulation, or foreground switching.
- **单标签串行写入**：所有咨询在一个专用标签页中串行执行；发送后状态不确定即停止，不自动再次提交。**Single-tab serial writes:** consultations execute serially in one dedicated tab; an uncertain post-submit state stops without another submission.
- **本地数据边界**：不保存页面 HTML、Cookie、令牌或其他 Edge 标签页正文；即时会话仅在用户选择的本地工作区保存实际发送与接收的 Markdown。**Local data boundary:** page HTML, cookies, tokens, and other Edge-tab content are not persisted; immediate conversations save sent and received Markdown only in a user-selected local workspace.
- **明确非目标**：不引入数据库、本地 Web 服务、后台守护进程、消息队列、通用 Provider 框架、在线更新、遥测后台、集中账号托管或管理员策略绕过。**Explicit non-goals:** no database, local web server, daemon, queue, generic provider framework, online updater, telemetry backend, shared-account hosting, or administrator-policy bypass.

## 文档 / Documentation

- [项目说明 / Project overview](./PROJECT-OVERVIEW.md)
- [完整项目设计 / Project design](./PROJECT-DESIGN.md)
- [阶段执行路线图 / Execution roadmap](./EXECUTION-ROADMAP.md)
- [安装说明 / Installation](./INSTALL.md)
- [团队部署与 G8 验收 / Team rollout and G8 validation](./TEAM-ROLLOUT.md)
- [故障排查 / Troubleshooting](./TROUBLESHOOTING.md)

## 本地开发 / Local development

修改代码前必须完整阅读 [PROJECT-DESIGN.md](./PROJECT-DESIGN.md) 与 [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md)。

Read [PROJECT-DESIGN.md](./PROJECT-DESIGN.md) and [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md) completely before changing code.

## 许可证与贡献 / License and contributions

本项目采用 [Apache License 2.0](./LICENSE) 发布。提交 Pull Request 前，请阅读[贡献指南](./CONTRIBUTING.md)；提交即表示你确认有权贡献，并同意你的贡献可按 Apache License 2.0 发布。第三方组件及其通知见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。

This project is licensed under the [Apache License 2.0](./LICENSE). Before opening a pull request, read the [contribution guide](./CONTRIBUTING.md); by submitting a contribution, you confirm that you have the right to contribute it and agree that it may be distributed under Apache License 2.0. See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for third-party components and notices.

## 说明 / Disclaimer

本项目是独立工具，并非 Microsoft、OpenAI 或 Anthropic 的官方产品，不隶属于、代表或获得其背书。它通过用户自己的已登录 Microsoft Edge 会话工作；使用者必须仅在自己获授权的账号、租户和组织策略范围内使用，并自行评估适用的服务条款、数据处理、合规与安全要求。请勿通过本项目发送不应交给 Microsoft 365 Copilot 处理的敏感数据。本项目按“现状”提供，不提供可用性、安全性、合规性或适配任何环境的保证。

This is an independent tool, not an official product of, affiliated with, representing, or endorsed by Microsoft, OpenAI, or Anthropic. It operates through the user's own signed-in Microsoft Edge session. Use it only with accounts, tenants, and organizational policies that authorize such use, and evaluate the applicable service terms, data handling, compliance, and security requirements yourself. Do not send sensitive data through this project if it should not be processed by Microsoft 365 Copilot. This project is provided "as is," without guarantees of availability, security, compliance, or suitability for any environment.

作者为独立开发者，项目仍在持续学习、验证与迭代中。架构设计、工程实现及 Vibe Coding 实践可能存在不足，欢迎通过 Issue、Discussion 或 Pull Request 提出问题、改进建议与贡献。

The author is an independent developer, and this project remains under active learning, validation, and iteration. Its architecture, engineering implementation, and Vibe Coding practices may have limitations. Issues, discussions, suggestions, and pull requests are welcome.
