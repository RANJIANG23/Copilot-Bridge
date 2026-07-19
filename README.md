# Copilot Bridge

Copilot Bridge 是一个面向 Windows 的本地桥接工具，让 Codex 通过当前用户已登录的 Microsoft Edge，在专用后台标签页中征询 Microsoft 365 Copilot，并将结果作为第二模型意见返回给 Codex。

Copilot Bridge is a local Windows bridge that lets Codex consult Microsoft 365 Copilot through a dedicated background tab in the user's signed-in Microsoft Edge profile, then returns the response to Codex as a second-model opinion.

```text
Codex → STDIO MCP → Copilot Bridge → Edge CDP/DOM → Microsoft 365 Copilot
```

日常咨询不会模拟鼠标键盘、抢占前台窗口或切换用户正在使用的 Edge 标签页。Copilot 只提供意见，最终核验、判断与执行仍由 Codex 完成。

Routine consultations do not simulate physical input, take foreground focus, or switch the user's active Edge tab. Copilot provides advice only; Codex remains responsible for verification, judgment, and execution.

## 当前状态 / Current status

| 项目 / Item | 状态 / Status |
|---|---|
| 开发版本 / Development version | `1.1.0-dev` |
| 发布状态 / Release status | 团队 v1 已通过；v1.1 会话工作台开发中 / Team v1 passed; v1.1 conversation workspace in development |
| 已通过 / Passed | Phase 0–6 and G1–G8（本机隔离验收 / local isolated acceptance） |
| 后续试点 / Follow-up pilot | 不同硬件、账号和企业策略环境 / Different hardware, account, and enterprise-policy environments |
| 平台 / Platform | Windows 11 x64 |

RC5 已达到项目定义的本机团队 v1 门禁，但不把本机隔离验收描述为跨设备兼容性证明。`1.1.0-dev` 尚未发布安装包；现有 RC5 下载仍是最后一个发布包。RC5 satisfies the project's local team-v1 gates, but local isolated acceptance is not presented as proof of cross-device compatibility. `1.1.0-dev` has not been packaged for release.

## 下载 / Download

从 [GitHub Releases](https://github.com/RANJIANG23/CopilotBridge/releases/tag/v1.0.0-rc.5) 下载以下两个文件。Download both files from [GitHub Releases](https://github.com/RANJIANG23/CopilotBridge/releases/tag/v1.0.0-rc.5):

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

## 使用前提 / Requirements

- Windows 11 x64
- Microsoft Edge 已登录成员自己的 Microsoft 365 企业账号 / Microsoft Edge signed in with the team member's own Microsoft 365 work account
- 该账号可以使用 Microsoft 365 Copilot / An account entitled to use Microsoft 365 Copilot
- Edge 从桌面或开始菜单正常启动默认配置档，并在 `edge://inspect` 中启用 Remote debugging，显示 `127.0.0.1:9222` / Edge launched normally from the desktop or Start menu with the default profile, with Remote debugging enabled at `edge://inspect` and showing `127.0.0.1:9222`
- Codex 桌面版或 Codex CLI / Codex desktop app or Codex CLI

Bridge 不保存或迁移账号、Cookie、令牌和 Microsoft 365 凭据。Bridge does not store or migrate accounts, cookies, tokens, or Microsoft 365 credentials.

## 快速安装 / Quick install

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

## 协作模式 / Collaboration modes

| 模式 / Mode | 中文说明 | Description |
|---|---|---|
| Assist | Codex 主导，对局部问题取得一次聚焦的第二意见 | Codex leads and obtains a focused second opinion on a specific issue |
| Outsource | Copilot 承担有限回合的开放式推理，Codex 最终核验 | Copilot performs bounded open-ended reasoning; Codex verifies the result |
| Review | 两个隔离 reviewer 串行审查，Codex 按证据裁决 | Two isolated reviewers run serially; Codex adjudicates using evidence |

协作模式只能在 GUI 中手动切换；v1 不自动选择或升级模式。

Collaboration modes can only be changed manually in the GUI. v1 does not select or escalate modes automatically.

模型优先级 / Model priority:

1. Opus
2. GPT 5.6 Think deeper
3. 深度思考 / Deep thinking

自动、快速答复和 GPT 5.5 快速响应不会进入候选队列。Auto, Quick response, and GPT 5.5 fast response are never eligible fallbacks.

## 安全与数据边界 / Security and data boundaries

- 浏览器交互只使用 Edge CDP 与专用 Copilot 标签页中的 DOM。Browser interaction uses Edge CDP and the DOM of the dedicated Copilot tab only.
- 不使用 Computer Use、OCR、Windows UI Automation 或物理输入模拟作为生产兜底。Computer Use, OCR, Windows UI Automation, and physical input simulation are not production fallbacks.
- 点击发送后的状态不确定时绝不自动重发。The bridge never resubmits after the submit state becomes uncertain.
- v1.1 的即时会话会在用户选择的本地工作区保存 Bridge 发送和接收的 Markdown 正文，并附带实际模型与状态；不会自动导入旧网页历史。v1.1 immediate conversations persist Bridge-sent and received Markdown in the user-selected local workspace, including the effective model and status; existing web history is never imported automatically.
- 页面 HTML、Cookie、令牌和其他 Edge 标签页正文仍不会保存。Page HTML, cookies, tokens, and content from other Edge tabs are never persisted.
- 不提供在线更新、遥测后台、团队账号托管或管理员策略绕过。There is no online updater, telemetry backend, shared-account hosting, or administrator-policy bypass.

## 文档 / Documentation

- [完整项目设计 / Project design](./PROJECT-DESIGN.md)
- [阶段执行路线图 / Execution roadmap](./EXECUTION-ROADMAP.md)
- [安装说明 / Installation](./INSTALL.md)
- [团队部署与 G8 验收 / Team rollout and G8 validation](./TEAM-ROLLOUT.md)
- [故障排查 / Troubleshooting](./TROUBLESHOOTING.md)

## 本地开发 / Local development

仓库有且只有一个生产项目、一个测试项目和一个生产可执行文件。不要恢复 Frozen 项目或增加第二套浏览器自动化。

The repository is intentionally limited to one production project, one test project, and one production executable. Do not restore the Frozen project or add a second browser-automation stack.

```powershell
dotnet build CopilotBridge.sln
dotnet test CopilotBridge.sln --no-build
```

生成内部 `win-x64` 自包含发布包。Build the internal self-contained `win-x64` release package:

```powershell
.\distribution\Build-Release.ps1
```

修改代码前必须完整阅读 [PROJECT-DESIGN.md](./PROJECT-DESIGN.md) 与 [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md)。

Read [PROJECT-DESIGN.md](./PROJECT-DESIGN.md) and [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md) completely before changing code.

## 说明 / Disclaimer

本项目是内部独立工具，并非 Microsoft 官方产品，也不隶属于或代表 Microsoft、OpenAI、Anthropic。

This is an independent internal tool. It is not an official product of, affiliated with, or endorsed by Microsoft, OpenAI, or Anthropic.
