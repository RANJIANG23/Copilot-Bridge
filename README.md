# Copilot Bridge

Copilot Bridge 是一个面向 Windows 的本地桥接工具，让 Codex 通过当前用户已登录的 Microsoft Edge，在专用后台标签页中征询 Microsoft 365 Copilot，并将结果作为第二模型意见返回给 Codex。

```text
Codex → STDIO MCP → Copilot Bridge → Edge CDP/DOM → Microsoft 365 Copilot
```

日常咨询不会模拟鼠标键盘、抢占前台窗口或切换用户正在使用的 Edge 标签页。Copilot 只提供意见，最终核验、判断与执行仍由 Codex 完成。

## 当前状态

- 版本：`1.0.0-rc.2`
- 状态：内部发布候选版
- Phase 0–5 与 G1–G7 已通过
- 团队 v1 仍需第二台电脑完成 G8 安装和 Assist 调用验证
- 平台：Windows 11 x64

不要将 RC2 描述为已经完成的稳定团队版。

## 下载

从 [GitHub Releases](https://github.com/RANJIANG23/CopilotBridge/releases/tag/v1.0.0-rc.2) 下载：

- `CopilotBridge-1.0.0-rc.2-win-x64.zip`
- `CopilotBridge-1.0.0-rc.2-win-x64.zip.sha256`

当前 ZIP SHA-256：

```text
dd91b4dc25f30502109b28723342ba57251d624a8614d31d1372d96358bad16b
```

安装前可在 PowerShell 中核对：

```powershell
(Get-FileHash .\CopilotBridge-1.0.0-rc.2-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

## 使用前提

- Windows 11 x64
- Microsoft Edge 已登录成员自己的 Microsoft 365 企业账号
- 该账号可以使用 Microsoft 365 Copilot
- Edge 当前实例已启用 Remote debugging，并显示 `127.0.0.1:9222`
- Codex 桌面版或 Codex CLI

Bridge 不保存或迁移账号、Cookie、令牌和 Microsoft 365 凭据。

## 快速安装

1. 完整解压 ZIP，不要只复制 EXE。
2. 在解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1
```

3. 从开始菜单打开 Copilot Bridge。
4. 确认 Edge 与 Microsoft 365 Copilot 状态正常，然后绑定专用 Copilot 标签页。
5. 首次建议使用“仅手动 + Assist”。
6. 保存设置，关闭 GUI，并新建一个 Codex 任务。
7. 要求 Codex 使用 `copilot-consult` 对一个具体方案进行二次核验。

完整步骤见 [安装说明](./INSTALL.md)。团队第二台电脑请同时执行 [G8 试点清单](./TEAM-ROLLOUT.md#试点验收g8)。

## 协作模式

| 模式 | 用途 |
|---|---|
| Assist | Codex 主导，对局部问题取得一次聚焦的第二意见 |
| Outsource | 让 Copilot 承担有限回合的开放式推理，Codex 最终核验 |
| Review | 两个隔离 reviewer 串行审查，Codex 按证据裁决 |

协作模式只能在 GUI 中手动切换。v1 不自动选择或升级模式。

模型优先级固定为：

1. Opus
2. GPT 5.6 Think deeper
3. 深度思考

自动、快速答复和 GPT 5.5 快速响应不会进入候选队列。

## 安全与数据边界

- 浏览器交互只使用 Edge CDP 与专用 Copilot 标签页中的 DOM。
- 不使用 Computer Use、OCR、Windows UI Automation 或物理输入模拟作为生产兜底。
- 点击发送后的状态不确定时绝不自动重发。
- 默认只保存咨询 ID、时间、模式、模型、状态和 conversation URL 等本地元数据。
- 不默认保存问题正文、回复正文、页面 HTML、Cookie 或令牌。
- 不提供在线更新、遥测后台、团队账号托管或管理员策略绕过。

## 文档

- [完整项目设计](./PROJECT-DESIGN.md)
- [阶段执行路线图](./EXECUTION-ROADMAP.md)
- [安装说明](./INSTALL.md)
- [团队部署与 G8 验收](./TEAM-ROLLOUT.md)
- [故障排查](./TROUBLESHOOTING.md)

## 本地开发

仓库有且只有一个生产项目、一个测试项目和一个生产可执行文件。不要恢复 Frozen 项目或增加第二套浏览器自动化。

```powershell
dotnet build CopilotBridge.sln
dotnet test CopilotBridge.sln --no-build
```

生成内部 `win-x64` 自包含发布包：

```powershell
.\distribution\Build-Release.ps1
```

修改代码前必须完整阅读 [PROJECT-DESIGN.md](./PROJECT-DESIGN.md) 与 [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md)。

## 说明

本项目是内部独立工具，并非 Microsoft 官方产品，也不隶属于或代表 Microsoft、OpenAI、Anthropic。
