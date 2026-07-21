# Copilot Bridge

Copilot Bridge 是一个面向 Windows 的本地协作桥接工具。它让 Codex 通过本机 STDIO MCP、当前用户已登录的 Microsoft Edge，与已绑定的 Microsoft 365 Copilot 专用后台标签页进行受控协作。根据用户在 GUI 中选择的协作模式，Copilot 可以承担前置思考、独立审核、与 Codex 相对独立的并行推理，或对明确问题提供聚焦协助；Codex 始终负责基于证据的最终裁决与实际执行。本项目在企业版 Microsoft 365 Copilot 高级版（国际版）环境中完成测试；如需面向个人版 Copilot 使用或调试，请自行验证并按其页面、功能和策略差异作相应调整。

Copilot Bridge is a local Windows collaboration bridge. It lets Codex work through local STDIO MCP and a dedicated, bound Microsoft 365 Copilot background tab in the user's signed-in Microsoft Edge session. Depending on the collaboration mode selected in the GUI, Copilot can provide upfront reasoning, independent review, a reasoning branch independent from Codex, or focused assistance; Codex remains responsible for evidence-based final judgment and execution. The project was tested with the international enterprise edition of Microsoft 365 Copilot premium; personal Copilot use or debugging requires independent verification and adjustments for its page, feature, and policy differences.

```text
Codex → STDIO MCP → Copilot Bridge → Edge CDP/DOM → Microsoft 365 Copilot
```

每次协作由三个彼此独立的控制面约束：**征询策略**决定何时允许咨询；**协作模式**决定 Assist、Outsource、Review 三种角色分工，且只能由用户在 GUI 中手动选择；**模型策略**决定允许使用的模型及优先级，并排除“自动”和快速响应类模型。“并行推理”指推理视角独立，不代表浏览器操作并发；实际 Copilot 会话始终在单一专用标签页中串行执行。

Every collaboration is governed by three independent control planes: **consultation policy** determines when a consultation is allowed; **collaboration mode** determines the Assist, Outsource, or Review division of work and can only be selected manually in the GUI; and **model policy** determines the allowed models and their priority while excluding Auto and quick-response models. “Parallel reasoning” means independent reasoning perspectives, not concurrent browser operations; Copilot sessions remain serial in one dedicated tab.

日常咨询不会模拟鼠标键盘、抢占前台窗口或切换用户正在使用的 Edge 标签页。Copilot 不执行本机操作，也不构成授权依据。

Routine consultations do not simulate physical input, take foreground focus, or switch the user's active Edge tab. Copilot does not execute local actions and is not an authorization source.

## 快速开始 / Quick start

### 下载 / Download

从 [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases) 下载以下两个同版本文件。Download both matching-version files from [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases):

- `CopilotBridge-1.3.0-win-x64.zip`
- `CopilotBridge-1.3.0-win-x64.zip.sha256`

ZIP 的 SHA-256 位于同名 `.sha256` 文件中。The ZIP SHA-256 is supplied in its matching `.sha256` file.

安装前可在 PowerShell 中核对。Verify it in PowerShell before installation:

```powershell
(Get-FileHash .\CopilotBridge-1.3.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
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

## 当前核心能力 / Current capabilities

- **受控后台协作**：只操作用户明确绑定的 Copilot 标签页；连接、模型选择、发送和回复提取均通过 Edge CDP 与 DOM 完成，不模拟鼠标键盘或抢占前台。**Controlled background collaboration:** only the explicitly bound Copilot tab is operated; connection, model selection, submission, and reply extraction use Edge CDP and DOM without simulated input or foreground takeover.
- **三种协作模式**：Assist 提供聚焦协助，Outsource 承担开放式前置推理，Review 使用两个串行且隔离的会话进行独立审核。协作模式和模型策略只能由用户在 GUI 中设置。**Three collaboration modes:** Assist provides focused help, Outsource performs open-ended upfront reasoning, and Review uses two serial, isolated conversations for independent review. Collaboration mode and model policy are controlled only through the GUI.
- **四个窄 MCP 工具**：状态与咨询工具之外，`search_conversations` 和 `read_conversation` 只在用户按项目授权的范围内检索或分页读取本地会话。项目默认关闭，可分别授权元数据、检索片段或完整会话。**Four narrow MCP tools:** alongside status and consultation, `search_conversations` and `read_conversation` search or page through local conversations only within project scopes authorized by the user. Projects are off by default and can separately allow metadata, snippets, or full conversations.
- **本地会话工作台**：即时咨询保存为人可读 Markdown，并以不重复正文的 sidecar 记录内部元数据；支持项目归类、检索、移动、改名、置顶、排序、复制，以及用户确认后的当前旧对话导入。**Local conversation workspace:** immediate consultations are stored as human-readable Markdown with body-free metadata sidecars, supporting project organization, search, move, rename, pin, ordering, copy, and user-confirmed import of the current legacy conversation.
- **明确的数据与迁移控制**：旧会话格式继续可读；迁移必须由用户显式触发，先备份并支持受保护的回滚。只读 MCP 调用不会迁移或改写工作区，Bridge 也不会自动把历史正文发送给 Copilot。**Explicit data and migration control:** legacy conversations remain readable; migration is user-initiated, backed up, and guarded for rollback. Read-only MCP calls do not migrate or modify the workspace, and Bridge never sends historical content to Copilot automatically.
- **桌面体验**：提供中英文界面、明暗主题、键盘排序和辅助技术语义、工作区与快捷方式入口，以及默认关闭的可选系统托盘。**Desktop experience:** includes Chinese and English UI, light and dark themes, keyboard ordering and assistive-technology semantics, workspace and shortcut actions, and an opt-in system tray that is off by default.
- **可靠性边界**：精确支持 `m365.cloud.microsoft` 与 `copilot.cloud.microsoft`；并发写入立即返回 busy，提交前失败会指明新建或复用咨询，提交状态不确定时禁止自动重发。已知页面浮层和未知遮挡返回稳定错误，不执行强制点击或自动关闭未知弹窗。**Reliability boundaries:** exactly supports `m365.cloud.microsoft` and `copilot.cloud.microsoft`; concurrent writes return busy, pre-submit failures distinguish new from reused consultations, and uncertain submission states are never retried automatically. Known overlays and unknown blockers return stable errors without forced clicks or automatic dismissal of unknown dialogs.

逐版本新增、修复、验证结果、安装包和 SHA-256 见 [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases)。For version-by-version changes, fixes, validation results, packages, and SHA-256 values, see [GitHub Releases](https://github.com/RANJIANG23/Copilot-Bridge/releases).

## 当前状态与限制 / Current status and limits

| 项目 / Item | 状态 / Status |
|---|---|
| 当前源码版本 / Current source version | `1.3.0` |
| 发布状态 / Release status | v1.3.0 已发布 Windows x64 自包含安装包与 SHA-256 文件 / v1.3.0 released with a Windows x64 self-contained package and SHA-256 file |
| 已通过 / Passed | Phase 0–28 and G1–G8 |
| 后续试点 / Follow-up pilot | 不同硬件、账号和企业策略环境 / Different hardware, account, and enterprise-policy environments |
| 平台 / Platform | Windows 11 x64 |

团队 v1.3.0 已达到项目定义的本机门禁，但不把本机隔离验收描述为跨设备兼容性证明。`1.3.0` 已作为 Windows x64 自包含安装包发布；安装前请核对 GitHub Release 中的同名 `.sha256` 文件。

Team v1.3.0 satisfies the project's local gates, but local isolated acceptance is not presented as proof of cross-device compatibility. `1.3.0` is released as a Windows x64 self-contained package; verify the matching `.sha256` file in the GitHub Release before installation.

## 架构开发思路 / Architecture and design rationale

Bridge 的设计优先保证用户控制、可验证性与最小运行边界，而不是构建通用 Agent 或浏览器自动化平台。

Bridge prioritizes user control, verifiability, and a minimal runtime boundary rather than becoming a general agent or browser-automation platform.

- **单一运行单元**：只有一个生产项目、一个生产可执行文件 `CopilotBridge.exe`；GUI 与 STDIO MCP 共享业务代码。**One production unit:** one production project and one executable, `CopilotBridge.exe`, with shared GUI and STDIO MCP business logic.
- **浏览器边界**：只经 Edge CDP 与绑定标签页 DOM 操作，不使用 Computer Use、OCR、Windows UI Automation、物理鼠标键盘模拟或前台窗口切换。**Browser boundary:** only Edge CDP and the bound-tab DOM are used; no Computer Use, OCR, Windows UI Automation, physical input simulation, or foreground switching.
- **单标签串行写入**：所有咨询在一个专用标签页中串行执行；发送后状态不确定即停止，不自动再次提交。**Single-tab serial writes:** consultations execute serially in one dedicated tab; an uncertain post-submit state stops without another submission.
- **共享咨询生命周期**：GUI 与 MCP 共用同一个 Coordinator；策略、预算、状态与失败语义在获取 Edge 页面前统一判定。**Shared consultation lifecycle:** GUI and MCP use one coordinator; policy, budget, state, and failure semantics are decided before an Edge page is acquired.
- **本地数据边界**：不保存页面 HTML、Cookie、令牌或其他 Edge 标签页正文；即时会话仅在用户选择的本地工作区保存实际发送与接收的 Markdown。**Local data boundary:** page HTML, cookies, tokens, and other Edge-tab content are not persisted; immediate conversations save sent and received Markdown only in a user-selected local workspace.
- **本地统计缓存**：Dashboard 只读取 Bridge 本地记录，刷新时预计算一次；日期与倍率切换复用缓存，不扫描 Microsoft 365 历史。**Local statistics cache:** the dashboard reads Bridge-local records only and precomputes once per refresh; date and multiplier changes reuse the cache and never scan Microsoft 365 history.
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
