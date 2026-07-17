# Copilot Bridge 安装

## 前提

- Windows 11 x64。
- Microsoft Edge 已使用团队成员自己的 Microsoft 365 账号登录 Copilot。
- Edge 已为当前浏览器实例启用 Remote debugging，并显示本地端口 `127.0.0.1:9222`。
- Codex 桌面版或可执行 `codex` 命令的 Codex CLI。

Copilot Bridge 不接管账号、Cookie 或 Edge 配置档，也不会自动修改企业策略。

## 安装

1. 解压整个 `CopilotBridge-1.0.0-rc.1-win-x64.zip`，不要只复制 EXE。
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

安装脚本不会直接编辑任何 `config.toml`；Codex CLI 只登记本项目的 marketplace 与 Plugin。它不会替换项目 `.codex\config.toml`，也不会删除或改写其他 Plugin/MCP 条目。重复运行同一命令即为修复或升级。

## 首次绑定

1. 保持 Edge 运行并确认 Remote debugging 为 `127.0.0.1:9222`。
2. 从开始菜单打开 Copilot Bridge。
3. 在诊断页确认 Edge、Microsoft 365 Copilot 登录和模型菜单均可用。
4. 绑定专用于 Bridge 的 Copilot 标签页。
5. 在设置页选择征询策略和协作模式；初始建议为“仅手动 + Assist”。
6. 保存并关闭窗口。后台咨询不依赖 GUI 保持打开。
7. 新建 Codex 任务，再要求 Codex 使用 `copilot-consult`。

如果 Edge 显示 Remote access 确认，这是浏览器本身的安全边界。Bridge 不会绕过该提示或企业管理员策略。

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
