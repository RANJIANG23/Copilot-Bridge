# Copilot Bridge 团队部署与 G8 验收

## 发布边界

本包是 Windows x64 内部团队 v1 候选版。每位成员使用自己的 Edge 配置档、Microsoft 365 登录和 Codex 环境；团队不共享账号、Cookie、咨询记录或浏览器数据。

发布者只需分发两项：

- `CopilotBridge-1.0.0-rc.5-win-x64.zip`
- 同名 `.sha256` 文件

不要从仓库的 `bin` 或 `obj` 目录拼装包，也不要单独发送 EXE。

## G8 本机隔离验收

G8 已由原来的第二台电脑门禁改为本机隔离验收。它由两个互补部分组成：

1. 在临时且独立的 `%LOCALAPPDATA%`、`CODEX_HOME` 和开始菜单目录中验证完整 ZIP 的安装、Plugin/MCP 启动、卸载和宿主配置保护。
2. 使用本机已登录的日常 Edge 与真实 Microsoft 365 Copilot，验证浏览器授权后的后台 Assist、模型返回、回复读取和例行咨询前台无抢占。

G8 通过必须同时满足：

- ZIP 哈希匹配；
- 隔离安装目录与开始菜单入口正确创建；
- 隔离 marketplace 和 Plugin 正确登记并启用；
- 安装后的 MCP 进程能够启动；
- 卸载后应用、快捷方式、Plugin 和 marketplace 均被清除；
- 宿主 Codex 配置、其他 Plugin/marketplace 与用户数据保持不变；
- 真实 Assist 返回非空回复、实际模型、consultation ID 和 conversation URL；
- `newConversation=true` 从新聊天页开始，不复用已绑定的旧 conversation URL；
- 用户完成 Edge Remote access 授权后，Copilot 工作标签页保持后台，例行咨询不被 Edge 或 Copilot Bridge 抢占前台；
- 点击发送后的不确定状态仍然零自动重试。

本机隔离验收不等于跨设备兼容性证明。不同硬件、Windows build、Microsoft 365 账号、tenant 和企业策略环境仍应在 v1 后的团队推广中继续试点，但不再阻塞本地团队 v1 门禁。

Edge 自身的 Remote access 授权提示可能进入前台并需要用户确认；Bridge 不绕过它，也不把这次浏览器安全授权计作后台例行咨询。授权后的咨询才用于前台无抢占验收。

## 成员安装

1. 按 [INSTALL.md](./INSTALL.md) 安装。
2. 使用自己的 Microsoft 365 企业账号登录 Edge 中的 Copilot。
3. 从桌面或开始菜单正常启动默认 Edge 配置档，再在 `edge://inspect` 中启用当前实例的 Remote debugging；不要为 Bridge 使用命令行参数拉起 Edge。
4. 在 Copilot Bridge GUI 完成诊断、绑定和设置保存。
5. 关闭 GUI，新建 Codex 任务。
6. 要求 Codex 执行一次 Assist，例如：`请使用 copilot-consult 对下面方案给出一次二次核验。`

## v1 后团队试点建议

每种新环境首次部署时记录：

- Windows 版本与 Edge 版本；
- Microsoft 365 账号/tenant 是否能看到目标模型；
- Remote debugging 是否能稳定进入 `127.0.0.1:9222`；
- 安装、首次绑定、Assist、后台无抢占和卸载是否通过；
- 失败发生在发送前、发送不确定还是回复读取阶段。

试点发现兼容性差异时，应修复现有 Edge CDP/DOM 路径，不增加 Computer Use、UI Automation、物理输入模拟或第二套浏览器自动化作为生产兜底。

## 升级与回退

- 升级：解压新版并重新运行 `Install-CopilotBridge.ps1`。脚本只替换本项目安装目录并重新安装本项目 Plugin。
- 回退：运行卸载脚本，再安装先前保留的完整 ZIP。
- 用户数据默认跨升级、卸载保留；不要把 `%LOCALAPPDATA%\CopilotBridge` 打入团队包。

本项目不提供在线更新、遥测后台、公共市场发布或管理员策略部署。
