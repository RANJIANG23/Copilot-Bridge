# Copilot Bridge 故障排查

按顺序检查。不要反复点击发送；当结果为 `submission_unknown`、`reply_timeout` 或 `canRetrySafely=false` 时不得自动重试。

## Edge 未连接

症状：状态显示 Edge/CDP 不可用，或连接 `127.0.0.1:9222` 失败。

1. 打开 Edge 的 Remote debugging 页面。
2. 确认勾选允许当前浏览器实例远程调试，并显示 `127.0.0.1:9222`。
3. 若 Edge 刚重启，重新启用并接受浏览器自身的 Remote access 提示。
4. 回到 GUI 重新运行诊断；不要修改 Edge user-data 目录里的文件。

## Remote debugging 一直停在 Starting

症状：通过带远程调试参数的命令或快捷方式拉起 Edge，允许 Remote access 后，页面仍长期显示 `Starting`。

1. 关闭这次由命令行参数拉起的 Edge 实例。
2. 从桌面或开始菜单正常启动成员日常使用的默认 Edge 配置档。
3. 打开 `edge://inspect`，进入 Remote debugging，并允许当前浏览器实例。
4. 等待页面明确显示 `127.0.0.1:9222`，再回到 Copilot Bridge 绑定。

Microsoft Edge 通用文档同时提供命令行参数和运行中浏览器页面授权两种方式，但 Copilot Bridge 团队 v1 的第二台电脑实测只把后一种作为支持路径。不要为绕过 `Starting` 修改 user-data 目录、企业策略或增加另一套浏览器自动化。

## Copilot 未登录

症状：诊断识别到登录页、无 Copilot 输入框，或咨询返回登录相关错误。

在同一个日常 Edge 配置档中手动打开 Microsoft 365 Copilot 并完成登录，然后重新绑定。Bridge 不保存、迁移或代填凭据。

## 工作标签页被关闭

症状：已绑定 URL 不再存在，状态提示目标页或会话不可用。

在 Edge 中打开一个新的 Microsoft 365 Copilot 标签页，再通过 GUI 重新绑定。Bridge 不会接管其他普通标签页。

## 模型不可用或发生回退

模型队列固定为：

1. Opus
2. GPT 5.6 Think deeper
3. 深度思考

菜单打开后至少等待 2 秒，最多按设置等待 6 秒。若前两项未加载、被禁用或不可用，选择“深度思考”是预期回退；三项均不可用时必须在发送前失败，不得用自动或快速答复代替。

## Codex 看不到 Skill 或工具

1. 确认安装脚本最后显示 Plugin 安装成功。
2. 执行 `codex plugin list`，应看到 `copilot-bridge@copilot-bridge-team` 为 installed/enabled。
3. 安装或升级后必须新建 Codex 任务；既有任务不会热加载 Plugin。
4. 如仍失败，重新运行安装脚本。它不会覆盖其他 Plugin 或 MCP 配置。

## 应用日志与本地数据

- 设置：`%LOCALAPPDATA%\CopilotBridge\settings.json`
- 咨询元数据：`%LOCALAPPDATA%\CopilotBridge\consultations.json`
- 日志：`%LOCALAPPDATA%\CopilotBridge\logs`

咨询正文、回复正文、HTML、Cookie 和令牌不写入上述文件。向支持人员发送日志前仍应自行检查内容。

## 修复安装

从原始完整 ZIP 重新运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1
```

修复或升级采用暂存目录；只有完整应用复制成功后才替换安装目录。替换后的 Plugin 注册若失败，安装器会恢复上一个应用版本和原 Plugin 注册，而不是留下半安装状态。

不要手动删除 Codex 的全局配置，也不要复制其他成员的 Edge 配置档。
