# Copilot Bridge 团队部署

## 发布边界

本包是 Windows x64 内部发布候选版。每位成员使用自己的 Edge 配置档、Microsoft 365 登录和 Codex 环境；团队不共享账号、Cookie、咨询记录或浏览器数据。

发布者只需分发两项：

- `CopilotBridge-1.0.0-rc.2-win-x64.zip`
- 同名 `.sha256` 文件

不要从仓库的 `bin` 或 `obj` 目录拼装包，也不要单独发送 EXE。

## 当前试点目标

| 项目 | 值 |
|---|---|
| G8 测试电脑 | Surface Laptop Studio 2 |
| 当前状态 | 核心闭环已通过：安装、Assist 发送与回复读取成功。 |
| 实机发现 | 命令行参数拉起的 Edge 在授权后可能停在 `Starting`；桌面正常启动的默认配置档工作正常。 |
| 下一步 | 补齐配置保护、前台无抢占和卸载恢复检查后，才能将 G8 标记为通过。 |

该记录指定 G8 的实际试点对象，但不包含局域网定位、设备名或个人运行记录，也不构成 G8 通过证据；完成下方所有验收项后才能更新项目状态。

## 成员操作

1. 按 [INSTALL.md](./INSTALL.md) 安装。
2. 使用自己的 Microsoft 365 企业账号登录 Edge 中的 Copilot。
3. 从桌面或开始菜单正常启动默认 Edge 配置档，再在 `edge://inspect` 中启用当前实例的 Remote debugging；不要为 Bridge 使用命令行参数拉起 Edge。
4. 在 Copilot Bridge GUI 完成诊断、绑定和设置保存。
5. 关闭 GUI，新建 Codex 任务。
6. 要求 Codex执行一次 Assist，例如：`请使用 copilot-consult 对下面方案给出一次二次核验。`

## 试点验收（G8）

在第二台团队电脑记录以下结果：

- ZIP 哈希匹配；
- 安装目录与开始菜单入口存在；
- 该成员原有 Edge 配置档和其他 Codex Plugin/MCP 均未改变；
- GUI 能识别、绑定日常 Edge 中的 Copilot；
- 使用桌面正常启动的默认 Edge 配置档时，Remote debugging 不停在 `Starting`；
- 新 Codex 任务能看到 `copilot-consult` 和两个 `copilot_bridge` 工具；
- 一次 Assist 返回非空回复、模型名称和 consultation ID；
- Copilot 工作标签页可保持后台，前台窗口、标签页、鼠标和键盘不被抢占；
- 卸载后本项目 Plugin/marketplace 和开始菜单入口消失，其他配置仍存在。

第二台电脑没有完成上述实测前，版本状态只能写为“发布候选版完成，G8 等待第二台团队电脑验证”。

## 升级与回退

- 升级：解压新版并重新运行 `Install-CopilotBridge.ps1`。脚本只替换本项目安装目录并重新安装本项目 Plugin。
- 回退：运行卸载脚本，再安装先前保留的完整 ZIP。
- 用户数据默认跨升级、卸载保留；不要把 `%LOCALAPPDATA%\CopilotBridge` 打入团队包。

本项目不提供在线更新、遥测后台、公共市场发布或管理员策略部署。
