# Copilot Bridge 通用 STDIO MCP 接入

> 适用版本：v1.3.2 及后续版本
> 状态：标准 STDIO MCP 契约；不代表已验证每一种第三方 Agent 客户端

## 1. 调用边界

Copilot Bridge 的 MCP server 由同一个 `CopilotBridge.exe` 提供。调用 Agent 负责启动进程、保持 STDIO 生命周期、解释结构化结果并最终核验 Copilot 建议。

```text
调用 Agent → STDIO MCP → CopilotBridge.exe --mcp
             → Edge CDP/DOM → Microsoft 365 Copilot
```

Bridge 不接收 Agent 身份、账号或密钥，不调度多个 Agent，也不提供端口、daemon、队列或通用 Provider 框架。

## 2. 启动命令

安装后的默认命令：

```text
%LOCALAPPDATA%\Programs\CopilotBridge\CopilotBridge.exe --mcp
```

开发构建可以直接使用对应输出目录中的 `CopilotBridge.exe --mcp`。工作目录应为 EXE 所在目录。客户端必须通过 STDIO 与进程通信，并在会话结束时关闭标准输入，让 server 正常退出。

如需隔离设置，可额外传入绝对路径：

```text
CopilotBridge.exe --mcp --settings-path C:\absolute\path\settings.json
```

本文件只定义命令和协议行为。各 Agent 产品的 MCP 配置文件、权限标签和安装方式不同，应按其官方文档配置；不要把未经验证的示例当作已支持集成。

## 3. 四个工具

| 工具 | 安全语义 | 说明 |
|---|---|---|
| `copilot_bridge_status` | 只读、非破坏、非 open-world | 读取 Bridge 配置和本地 Edge/CDP 就绪摘要，不发送消息 |
| `search_conversations` | 只读、非破坏、非 open-world | 只检索 GUI 已授权项目 |
| `read_conversation` | 只读、非破坏、非 open-world | 只分页读取一个明确且具有 Full 权限的会话 |
| `consult_copilot` | 写入、破坏性、open-world | 向用户自己的 Microsoft 365 Copilot 会话发送一条消息 |

调用方不得把 `consult_copilot` 标成只读来规避审批。发送无法由 Bridge 撤回。

## 4. 征询 trigger

`consult_copilot.trigger` 接受：

- `user_explicit`：用户明确要求本次征询。
- `agent_auto`：调用 Agent 按 GUI 策略发起自动检查点。
- `required_checkpoint`：关键设计必须征询。
- `codex_auto`：为旧 Codex Plugin 保留的兼容别名，语义与 `agent_auto` 相同。

“仅手动”策略只允许 `user_explicit`。调用 Agent 不得把自动决策伪装成用户明确要求。

为保持补丁版本兼容，`copilot_bridge_status.consultationPolicy` 在 v1.3.2 仍可能返回旧值 `codex_may_consult`；它表示 GUI 中的“Agent 可自动征询”，不限制实际调用方。

## 5. 重试与会话复用

- 调用前先读取 `copilot_bridge_status`。
- 完成后的追问复用返回的 `consultationId`。
- `canRetrySafely=false` 时绝不重试。
- `retryAction=reuse_consultation` 时复用原 ID。
- `retryAction=new_consultation` 时省略 ID。
- `retryAction=none` 时停止。
- `submission_unknown` 和其他发送后失败禁止自动重发。

Copilot 只提供意见。调用 Agent 必须核验事实、裁决建议并负责实际执行。

## 6. 安装选择

默认安装命令会同时安装已验证的 Codex Plugin。其他 STDIO MCP 客户端可以先执行应用单独安装：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CopilotBridge.ps1 -SkipCodexPlugin
```

然后按该客户端的官方 MCP 配置方式登记上面的 EXE 与 `--mcp` 参数。这个路径不会安装或声明 WorkBuddy、OpenClaw、opencode 或其他第三方适配器。
