# Copilot Bridge 安装

## 前提

- Windows 11 x64。
- Microsoft Edge 已使用团队成员自己的 Microsoft 365 账号登录 Copilot。
- Edge 由桌面或开始菜单正常启动，并使用成员日常默认配置档。
- Edge 已在 `edge://inspect` 的 Remote debugging 页面为当前浏览器实例启用远程调试，并显示本地端口 `127.0.0.1:9222`。
- Codex 桌面版或可执行 `codex` 命令的 Codex CLI。

Copilot Bridge 不接管账号、Cookie 或 Edge 配置档，也不会自动修改企业策略。

## 安装

1. 解压整个 `CopilotBridge-1.0.0-rc.5-win-x64.zip`，不要只复制 EXE。
2. 可选：用发布者提供的 `.sha256` 文件核对 ZIP 的 SHA-256。
3. 在解压目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1
```

安装器会：

- 把应用复制到 `%LOCALAPPDATA%\Programs\CopilotBridge`；
- 创建当前用户的“Copilot Bridge”开始菜单入口；
- 注册仅包含本项目的 `copilot-bridge-team` 本地 marketplace；
- 安装 `copilot-bridge` Plugin。

安装脚本不会直接编辑任何 `config.toml`；Codex CLI 只登记本项目的 marketplace 与 Plugin。它不会替换项目 `.codex\config.toml`，也不会删除或改写其他 Plugin/MCP 条目。重复运行同一命令即为修复或升级。如果升级过程中 Plugin 注册失败，安装器会恢复先前的应用目录和 Plugin 注册；若回退本身不完整，则明确报错，且不会清理尚未恢复的应用备份。

## 首次绑定

1. 从桌面或开始菜单正常启动日常 Edge，不要为 Bridge 使用带 `--remote-debugging-port` 的命令行快捷方式。
2. 打开 `edge://inspect` 的 Remote debugging 页面，允许当前浏览器实例，并确认显示 `127.0.0.1:9222`。
3. 从开始菜单打开 Copilot Bridge。
4. 在诊断页确认 Edge、Microsoft 365 Copilot 登录和模型菜单均可用。
5. 绑定专用于 Bridge 的 Copilot 标签页。
6. 在设置页选择征询策略和协作模式；初始建议为“仅手动 + Assist”。
7. 保存并关闭窗口。后台咨询不依赖 GUI 保持打开。
8. 新建 Codex 任务，再要求 Codex 使用 `copilot-consult`。

如果 Edge 显示 Remote access 确认，这是浏览器本身的安全边界。Bridge 不会绕过该提示或企业管理员策略。

实测发现：使用命令行参数拉起 Edge 时，授权后 Remote debugging 可能一直停在 `Starting`；改为从桌面正常启动默认配置档后工作正常。因此团队 v1 只支持后一条已验证路径，应用不会自动带参数启动 Edge。

## 应用单独安装

只需要 GUI、不安装 Codex Plugin 时可执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1 -SkipCodexPlugin
```

## 卸载

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\CopilotBridge\Uninstall-CopilotBridge.ps1"
```

默认保留 `%LOCALAPPDATA%\CopilotBridge` 下的设置、诊断日志和仅含元数据的咨询记录。彻底删除时加 `-RemoveUserData`。

卸载器只有在确认属于本项目的 Plugin 与 marketplace 可以安全移除后才删除应用。如果 Codex 注册查询失败，卸载会停止且保留现有安装；如果 marketplace 移除失败，已移除的 Plugin 会先恢复。
