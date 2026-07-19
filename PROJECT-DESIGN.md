# Microsoft Copilot 项目完整设计

> 设计基线：v1.2.0-dev
> 日期：2026-07-20（Asia/Shanghai）
> 状态：v1.1.2 已发布；v1.2.0 Phase 15–16 已通过，项目权限与本地查询核心已建立
> 工作名称：Copilot Bridge
> 项目目录：本仓库根目录
> 目标模式执行路线图：[EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md)

## 1. 项目结论

本项目要做的不是一个通用浏览器自动化平台，也不是另一个多 Agent 框架，而是一个边界明确的 Windows 工具：

> 让 Codex 通过本机已登录的日常 Microsoft Edge，在后台与 Microsoft 365 Copilot 对话，并把回复作为第二模型意见返回给 Codex；Codex始终负责最终判断与实际执行。

最终交付由三部分组成：

1. **Copilot Bridge Windows 应用**：图形化配置、连接诊断、后台网页交互和本地 MCP 服务。
2. **Codex Skill**：规定何时征询、怎样组织上下文、怎样处理 Copilot 回复。
3. **Codex Plugin**：把 Skill 与 MCP 配置打包，供本人和团队成员安装。

核心技术路线固定为：

> `Codex → 本地 STDIO MCP → Copilot Bridge → Edge CDP/DOM → Microsoft 365 Copilot`

日常运行不使用 Computer Use、屏幕识别、Windows UI Automation、物理鼠标键盘模拟或前台窗口切换。

## 2. 已确认的产品决策

| 决策项 | v1 结论 |
|---|---|
| 浏览器 | 团队日常使用的 Microsoft Edge 与现有登录状态 |
| Copilot 地址 | `https://m365.cloud.microsoft/chat/` |
| 自动化方式 | Edge CDP + DOM，只控制一个专用 Copilot 标签页 |
| 前台占用 | 禁止抢占窗口、切换用户当前标签页、移动鼠标或注入物理键盘输入 |
| 协作模式 | Assist、Outsource、Review；由用户在 GUI 中手动选择 |
| 自动路由模式 | v1 不实现；Codex 不得自行切换协作模式 |
| 模型优先级 | Opus → GPT 5.6 Think deeper → 深度思考 |
| 禁用模型 | 自动、快速答复、GPT 5.5 快速响应及其他快速模式 |
| 模型菜单加载 | 打开后至少等待 2 秒，再观察菜单稳定；总等待默认最多 6 秒 |
| 发送确认 | 日常咨询不逐条询问；通过 Codex 的 MCP 单工具审批策略预先授权 |
| 图形界面 | Microsoft Copilot 的中性层级、留白、圆角卡片与轻量动效 |
| 团队复用 | Windows 应用与 Codex Plugin 组合分发 |
| v1 数据形式 | 纯文本/Markdown；不做文件、图片和仓库批量上传 |
| 会话默认 | 一个 Codex 任务对应一个 Copilot 咨询会话；后续追问复用咨询 ID |

最后两项是本设计为未决问题采用的默认假设，未来可以调整，但不影响首个闭环。

## 3. 术语与三个独立控制面

三个控制面必须在代码、GUI 和文档中保持独立。

### 3.1 征询策略：什么时候调用 Copilot

GUI 名称使用“征询策略”，避免与 Copilot 的“自动”模型混淆。

| 策略 | 行为 |
|---|---|
| 关闭 | 拒绝所有咨询调用 |
| 仅手动 | 只有用户明确要求“问 Copilot/Opus”时才允许调用；首次安装默认值 |
| Codex 可自动征询 | 用户明确要求时必定允许；复杂架构、重大方案或明显不确定时，Codex也可以主动调用 |
| 关键设计必须征询 | 对新项目架构、重大重构和高影响决策设置强制核验点；调用失败必须向用户说明，不得静默跳过 |

### 3.2 协作模式：怎样分工

协作模式只能由用户在 GUI 中选择：

- **Assist**：Codex 主导，Copilot 回答一个聚焦问题。
- **Outsource**：Copilot 负责主要的开放式推理或长方案，Codex提供上下文并最终核验。
- **Review**：使用相互隔离的 Copilot 会话执行独立审查，Codex 汇总分歧并裁决。

硬性规则：

- MCP 的 `consult_copilot` 工具不接受 `mode` 参数。
- Codex 不能在工具调用中临时改模式。
- GUI 修改只影响下一次咨询，不改变正在进行的咨询。
- v1 不实现 Assist → Outsource → Review 的自动升级。

### 3.3 模型策略：实际向哪个模型发送

默认优先级队列：

1. Opus
2. GPT 5.6 Think deeper
3. 深度思考

这个队列与征询策略、协作模式无关。即使征询策略为“Codex 可自动征询”，也绝不选择 Copilot 页面中的“自动”模型。

## 4. 目标、非目标与成功标准

### 4.1 v1 目标

- 在不抢占用户前台操作的情况下，连接日常 Edge 中已登录的 Microsoft 365 Copilot。
- 在后台专用标签页中可靠完成模型选择、消息发送、生成等待与 Markdown 回复提取。
- 支持手动选择的 Assist、Outsource、Review 三种协作模式。
- 支持用户明确调用，以及按 GUI 策略允许 Codex 在复杂任务中主动调用。
- 通过本地 STDIO MCP 暴露最小工具面。
- 提供简洁 GUI，完成配置、状态查看、连接诊断与测试咨询。
- 可以打包给使用 Edge 和 Microsoft 365 企业账号的团队成员。

### 4.2 明确非目标

v1 不做以下内容：

- 通用网页自动化平台或通用 Provider 框架。
- 直接调用未公开或逆向得到的 Microsoft 内部 API。
- 网络抓包、HTTP/WebSocket 劫持、代理中间人或请求重放。
- Computer Use、OCR、截图识别、`SendInput`、UIA 或前台鼠标点击兜底。
- 自研 Worker 协议、命名管道 RPC、本地 HTTP 服务、消息队列或数据库。
- 多机器调度、云服务、集中账号托管或远程控制。
- 自动选择 Assist/Outsource/Review。
- 文件附件、图片、整仓库上传或知识库同步。
- DLP、复杂脱敏引擎或企业审计平台。
- 完整保存 Copilot 对话正文。
- macOS、Linux 或非 Edge 浏览器支持。

### 4.3 产品成功标准

只有同时满足以下条件，项目才算达到 v1：

1. 用户正常使用鼠标、键盘和其他 Edge 标签页时，连续 10 次咨询不发生前台抢占。
2. 10 次咨询没有重复发送；任何发送状态不确定时都不自动重试。
3. 延迟加载的模型菜单能够按既定优先级选择并验证实际模型。
4. Edge 未启动、远程调试未启用、登录失效和模型不可用时均返回可理解的错误。
5. Codex 可以通过 MCP 发起咨询、读取结构化结果并继续完成原任务。
6. 本机隔离环境能够按文档完成安装、Plugin/MCP 启动和卸载，且用户完成 Edge 远程访问授权后，本机真实日常 Edge 能完成后台咨询；不同硬件、账号和企业策略环境作为 v1 后团队试点。

## 5. 总体架构

```mermaid
flowchart LR
    U["用户"] -->|"选择征询策略与协作模式"| GUI["Copilot Bridge GUI"]
    C["Codex"] -->|"Skill 组织上下文"| MCP["本地 STDIO MCP"]
    GUI --> CFG["本地配置"]
    MCP --> CFG
    MCP --> CORE["Consultation Coordinator"]
    GUI -->|"测试咨询"| CORE
    CORE --> EDGE["Edge Session Adapter"]
    EDGE -->|"CDP + DOM"| TAB["专用后台 Copilot 标签页"]
    TAB --> WEB["Microsoft 365 Copilot"]
    WEB --> TAB --> EDGE --> CORE --> MCP --> C
    C -->|"核验、裁决、执行"| RESULT["用户任务结果"]
```

### 5.1 进程模型

只发布一个生产可执行文件：`CopilotBridge.exe`。

- 正常启动：打开 WPF GUI。
- `CopilotBridge.exe --mcp`：作为无窗口 STDIO MCP server 运行。
- `CopilotBridge.exe --probe`：开发阶段执行连接与 DOM 探测；稳定后可以保留为诊断命令，但不增加第二个程序。

GUI 不必常驻，Codex 启动的 MCP 进程可以独立工作。两者共享配置文件；命名互斥锁只用于阻止两个进程同时向 Copilot 写入，不承担任务队列职责。

### 5.2 技术栈

| 层 | 选择 | 原因 |
|---|---|---|
| 运行时 | C# / .NET 10，`net10.0-windows` | 当前机器已具备 SDK；适合单文件 Windows 应用和企业部署 |
| GUI | WPF | 成熟、轻量、无需引入 WinUI 打包复杂度，足以实现 Microsoft Copilot 风格 |
| 浏览器 | Microsoft.Playwright for .NET，通过 `ConnectOverCDPAsync` 连接 Edge | 使用现有浏览器状态并执行 DOM 级操作 |
| MCP | 官方 C# MCP SDK，STDIO transport | 与 Codex 本地客户端直接集成，无需端口和后台服务 |
| 配置 | `System.Text.Json` | 无数据库，配置可读、可迁移、易诊断 |
| 测试 | xUnit + 静态 DOM fixtures | 覆盖状态机与延迟加载，不依赖每次真实发送 |
| 日志 | `Microsoft.Extensions.Logging` 的简单文件/控制台输出 | 只保留必要诊断，不建事件平台 |

实现时锁定当时的稳定包版本；不在设计阶段预先固定可能已变化的 NuGet 补丁版本。

### 5.3 内部组件

生产代码只设五个主要职责，不为每个类创建一层抽象：

1. **Settings Store**：读写 GUI 配置，原子替换 JSON。
2. **Edge Session Adapter**：连接 Edge、发现并绑定专用 Copilot 标签页。
3. **Copilot Page Driver**：模型选择、编辑器写入、发送、回复提取。
4. **Consultation Coordinator**：执行三种协作模式、会话复用和错误边界。
5. **MCP Host / GUI Shell**：两个入口共用上述业务代码。

只在真正的外部边界使用接口，例如浏览器驱动和时钟；不建立通用 Provider、Transport、Worker、Job、Artifact 等抽象体系。

## 6. 浏览器与后台标签页设计

### 6.1 连接方式

应用连接正在运行的日常 Edge 实例：

1. 用户在 Edge 中为当前实例启用远程调试。
2. 应用从日常 Edge user-data 目录的 `DevToolsActivePort` 获取本机端点。
3. Playwright 使用 CDP 连接现有实例，且不创建新的浏览器配置档。
4. 只枚举识别 Copilot 目标所必需的页面元数据。
5. 远程调试端点只允许本机访问。

v1 不自动启动 Edge，也不在 Edge 关闭后尝试带参数重新启动日常配置档，避免弹出窗口或选择错误的浏览器配置档。

### 6.2 一次性绑定

首次设置流程：

1. 用户在希望使用的 Edge 配置档中打开 Microsoft 365 Copilot 并完成登录/MFA。
2. GUI 显示当前发现的合法 Copilot 标签页。
3. 用户选择一个标签页并点击“绑定”。
4. 应用在该页设置本地标记，并在本次 Edge 生命周期内记录 CDP target 与 browser context。
5. 此后所有操作只在该标签页中完成。

如果标签页在 **同一次 Edge 生命周期** 中被关闭，应用可以在已绑定的 browser context 内调用 CDP `Target.createTarget`，使用 `background=true` 新建后台标签页；创建后仍须验证用户当前窗口和标签页未变化。当前 CDP 文档明确提供后台 target 参数，而 Edge 的 DevTools Protocol 与 Chrome DevTools Protocol 对齐。

如果 Edge 已重启，原 browser context ID 不再可信，应用返回“需要重新绑定”，不猜测另一个配置档。若实机验证发现后台 target 仍会抢占前台，则禁用自动重建并要求重新绑定，不能改用前台点击兜底。

### 6.3 标签页边界

每次准备修改页面前必须同时验证：

- scheme 为 HTTPS；
- host 精确等于 `m365.cloud.microsoft`；
- path 为 `/chat/` 或 `/chat/conversation/{id}`；
- 页面仍带有 Bridge 标记；
- 页面中存在唯一且可编辑的消息输入框。

任一条件不满足就停止，不点击其他页面，也不搜索其他网站。

### 6.4 无前台抢占的硬规则

生产代码禁止调用：

- Playwright `BringToFrontAsync`；
- Win32 `SetForegroundWindow`、`SendInput`；
- Windows UI Automation；
- Computer Use、截图识别或 OCR；
- 操作系统级鼠标、键盘事件；
- 为恢复失败而切换用户当前 Edge 标签页。

DOM 中的 `click`、`fill` 和 `press` 只作用于后台页面内部，不模拟物理设备。若 CDP/DOM 无法完成操作，结果必须失败，不能退化为前台自动化。

## 7. 模型选择状态机

### 7.1 选择算法

```mermaid
stateDiagram-v2
    [*] --> ReadCurrent
    ReadCurrent --> Verified: "当前已是允许模型"
    ReadCurrent --> OpenMenu: "当前为自动/快速/未知"
    OpenMenu --> MinimumWait: "打开模型菜单"
    MinimumWait --> Observe: "至少等待 2000 ms"
    Observe --> Observe: "菜单仍变化且未超时"
    Observe --> TryOpus: "连续快照稳定或达到观察条件"
    TryOpus --> Verify: "Opus 可见且启用"
    TryOpus --> TryGpt: "Opus 不可用"
    TryGpt --> Verify: "GPT 5.6 Think deeper 可见且启用"
    TryGpt --> TryDeep: "GPT 5.6 不可用"
    TryDeep --> Verify: "深度思考可见且启用"
    TryDeep --> Failed: "无允许模型"
    Verify --> Verified: "控件文本/选中状态吻合"
    Verify --> Failed: "无法验证"
    Verified --> [*]
    Failed --> [*]
```

默认参数：

- 菜单最短等待：2000 ms；
- 观察轮询间隔：250 ms；
- 稳定条件：连续两个标准化菜单快照相同；
- 菜单最长等待：6000 ms；
- 模型选择后验证超时：3000 ms。

“达到稳定”不允许突破 2 秒最短等待，因为 Opus 与 GPT 详细菜单可能在 1–3 秒后才水合出来。

### 7.2 精确匹配规则

允许中英文别名，但最终匹配必须是标准化后的完整标签，不使用模糊的 `Contains("GPT")`：

- `Opus` / `Claude Opus`；
- `GPT 5.6 Think deeper` / 对应中文标签；
- `深度思考` / `Deep thinking`。

以下标签永不进入候选集：

- 自动 / Auto；
- 快速答复、快速回答、Quick response、Instant；
- GPT 5.5 快速响应；
- 其他名称含“快速/Quick/Instant”的模型。

GPT 5.6 位于 GPT 子菜单时，必须先确认父项为 GPT，再等待子菜单稳定并选择完整的 `GPT 5.6 Think deeper`。绝不能因为只看到 GPT 父项就默认选择其第一个子项。

### 7.3 发送前不变量

只有全部满足时才允许发送：

- 当前模型已读回并属于允许列表；
- 消息输入框唯一且可编辑；
- 页面不处于上一条回复生成中；
- 输入框内容与待发送 Markdown 完全一致；
- 当前用户消息数量和最后一条消息指纹已记录；
- 已取得单写入互斥锁。

无法验证时发送零条消息，并返回 `not_submitted`。

## 8. 一次性发送与回复提取

### 8.1 发送语义

发送动作采用明确的前后边界：

1. **点击发送前**：允许重新解析 DOM、重新连接一次或安全重试读取。
2. **点击发送后**：永不自动再次点击、按 Enter 或重新提交。
3. 如果点击后的页面状态无法判断，返回 `submission_unknown`，由 Codex 告知用户检查原会话。

不得通过在 prompt 中插入内部幂等 ID 来污染对话。幂等性依靠页面回读、消息计数和“发送后不重试”保证。

### 8.2 回复完成判定

发送后记录原有 assistant turn 数量，然后等待新 turn：

- 出现新的 assistant turn；
- 该 turn 不再包含生成中指示器；
- 回复动作控件（例如复制）出现，或正文达到稳定条件；
- 正文间隔 750 ms 的两次读取相同；
- 未出现页面错误、登录页或会话不可用提示。

默认回复超时 300 秒，可在高级设置中调整到 60–900 秒。超时返回已有会话 URL 和 `reply_timeout`，但不再次发送。

### 8.3 Markdown 提取

只从当前新增 assistant turn 的渲染 DOM 提取：

- 标题、段落、列表、引用；
- 行内代码和代码块；
- 链接文本与 URL；
- 简单表格。

不读取整个页面文本，不把侧栏、推荐问题、按钮标签混入回复。Frozen 项目中的渲染 Markdown 提取思路可以重新实现并用 fixtures 验证，但不引用旧项目程序集。

## 9. 三种协作模式

### 9.1 Assist

用途：快速第二意见、局部风险检查、替代解释。

- Codex 仍是主执行者。
- 初始请求只包含一个明确问题。
- 默认最多 2 个 Copilot turn（初答 + 1 次聚焦追问）。
- 回复返回后，Codex必须说明采纳、部分采纳或拒绝了什么。

典型问题：

- “这段路由器配置有没有遗漏的回滚风险？”
- “这个模块边界是否出现了不必要的抽象？”
- “请指出这个诊断结论最可能错在哪里。”

### 9.2 Outsource

用途：开放式架构探索、长方案、复杂问题模拟。

- Skill 先把任务、现状、约束、非目标和期待输出组织成一个 Markdown 包。
- Copilot承担主要推理，但不能直接执行本机操作。
- 默认最多 6 个 Copilot turn；第 3 个 turn 后 Codex先检查是否仍有实质进展。
- Codex必须使用本地代码、日志或官方资料验证关键事实，再形成最终方案。

### 9.3 Review

用途：重大架构、发布前审查、需要独立意见的设计。

- 默认创建 2 个相互隔离的 Copilot 对话，串行执行，不并发操作网页。
- Reviewer A 角色：方案复杂度、边界和替代设计审查。
- Reviewer B 角色：故障模式、证据与可验证性审查。
- 两位 reviewer 均使用同一允许模型优先级；v1 不刻意为多样性选择较低优先级模型。
- Codex按证据裁决，不按多数票决定。
- 只有存在具体矛盾时，允许再向其中一个会话进行 1 次定向追问。

Review 的会话隔离通过新建 Copilot conversation 实现，可以在同一个专用后台标签页中串行导航，不创建多个自动化标签页。

## 10. Codex Skill 设计

Skill 负责工作流，不负责浏览器技术细节。建议名称：`copilot-consult`。

### 10.1 Skill 的触发规则

显式触发：

- 用户说“问一下 Copilot/Opus”；
- 用户要求第二意见、独立审查或多模型讨论。

允许自动触发的条件（还必须满足 GUI 征询策略）：

- 新项目或重大重构的宏观架构刚形成；
- 方案包含多个不可逆或高影响操作；
- Codex发现关键假设无法用本地证据直接验证；
- 方案明显可能出现过度工程化、过度防御性代码或范围膨胀。

不自动触发：

- 简单命令、版本查询和局部文本修改；
- 已有明确测试结果的常规 bug 修复；
- 只是为了“多一个观点”而没有具体问题；
- 同一决策点已经咨询过且没有新证据。

### 10.2 上下文包格式

Skill 将上下文整理为：

```markdown
# 任务

# 已知事实与证据

# 当前方案或争议点

# 约束与明确非目标

# 希望你回答的问题

# 期望输出格式
```

默认只发送与问题有关的摘要和必要代码片段，不上传整个仓库。应用层不会自行扩写、删改或总结该 Markdown。

### 10.3 Codex 的后处理责任

拿到回复后，Codex必须：

1. 区分 Copilot 提供的事实、推断和建议。
2. 用本地状态、测试或官方文档核验可验证事实。
3. 明确列出采纳与不采纳的部分。
4. 自己完成代码、命令、配置和最终答复。

Copilot 回复不是执行授权，也不能覆盖用户边界。

## 11. MCP 工具设计

v1 只暴露两个工具。

### 11.1 `copilot_bridge_status`

用途：读取当前连接与配置摘要，不触发网页写入。

输入：无。

输出包含：

- 应用版本；
- Edge/CDP 连接状态；
- 专用标签页绑定状态；
- 登录状态；
- 当前征询策略；
- GUI 选择的协作模式；
- 模型优先级；
- 是否有咨询正在执行。

注解：`readOnlyHint=true`、`destructiveHint=false`、`openWorldHint=false`。

### 11.2 `consult_copilot`

输入：

```json
{
  "requestMarkdown": "string",
  "trigger": "user_explicit | codex_auto | required_checkpoint",
  "consultationId": "optional string",
  "newConversation": false
}
```

设计约束：

- 不包含 `mode` 或 `model` 参数；两者由 GUI 配置决定。
- `trigger` 用于执行征询策略；“仅手动”会拒绝 `codex_auto`。
- 未传 `consultationId` 时创建新咨询。
- 传入 ID 时复用对应 Copilot conversation。
- `newConversation=true` 显式结束复用并新建会话。

统一输出：

```json
{
  "status": "completed | not_submitted | submission_unknown | reply_timeout | blocked",
  "errorCode": "optional stable code",
  "consultationId": "string",
  "collaborationMode": "assist | outsource | review",
  "responses": [
    {
      "reviewer": "primary | complexity | evidence",
      "effectiveModel": "opus | gpt_5_6_think_deeper | deep_thinking",
      "conversationUrl": "string",
      "markdown": "string"
    }
  ],
  "canRetrySafely": false
}
```

`canRetrySafely` 只有在明确尚未点击发送时才为 `true`。

注解必须诚实：`readOnlyHint=false`、`destructiveHint=true`、`openWorldHint=true`。消息发送到外部企业服务且无法由本工具撤回，因此不能为了减少审批而错误标记为只读。

### 11.3 MCP server instructions

初始化说明的前 512 字符必须独立表达最关键规则：

- 协作模式只由 GUI 决定；
- 发送状态不确定时禁止重试；
- 追问必须复用返回的 `consultationId`；
- Copilot 只提供意见，Codex负责核验和执行。

## 12. 审批与自动调用

用户要求日常发送前不逐条确认。实现方式不是把写操作伪装成只读，而是对唯一写工具进行明确的单工具预授权：

```toml
[mcp_servers.copilot_bridge]
command = "CopilotBridge.exe"
args = ["--mcp"]
default_tools_approval_mode = "prompt"

[mcp_servers.copilot_bridge.tools.consult_copilot]
approval_mode = "approve"
```

状态查询保持普通只读工具。团队版 Plugin 使用同等的 per-tool policy，但具体配置路径在打包阶段按当时 Plugin 规范生成。

企业管理员策略仍可能强制审批；应用不能也不会绕过组织政策。安装向导应检测实际策略并清楚显示“已预授权”或“受管理员策略限制”。

## 13. GUI 产品设计

### 13.1 设计语言

采用 **Microsoft Copilot 的中性、低干扰视觉语言**：

- 浅色使用近白画布、白色内容面和浅灰选择态；深色使用连续的炭灰层级，避免纯黑大面积断层；
- 导航、设置与对话管理使用中性灰卡片和胶囊按钮，主次动作依靠层级、字重和边框区分，不依赖高饱和品牌色；
- 蓝、绿、黄、红只用于链接、健康、警告与错误等语义状态；
- 中文界面正文统一使用 `Noto Sans SC`，英文回退到 `Segoe UI Variable Text`；图标使用 `Segoe Fluent Icons`；
- 内容卡片使用 10–20 px 圆角、细边框、轻分隔和充足留白；
- 默认无深色技术控制台、无渐变大卡片、无密集仪表盘；
- 拖拽与状态过渡只使用轻量透明度、位移和缩放，时长约 90–190 ms；
- 支持键盘导航、清晰焦点和 Windows 缩放。

建议基础 token：

| Token | 值 |
|---|---|
| Canvas light / dark | `#FAFAFA` / `#1F1F1F` |
| Surface light / dark | `#FFFFFF` / `#242424` |
| Selected light / dark | `#F3F3F3` / `#333333` |
| Primary action light / dark | `#242424` / `#F5F5F5` |
| Success | `#28C76F` |
| Warning | `#F5A524` |
| Error | `#E5484D` |
| Text primary light / dark | `#242424` / `#F5F5F5` |
| Text secondary light / dark | `#616161` / `#BDBDBD` |
| Divider light / dark | `#E5E5E5` / `#3D3D3D` |
| Corner radius | 10–20 px |

### 13.2 页面结构

#### 概览

- Edge 连接、专用标签页、登录和 MCP 状态；
- 当前征询策略、协作模式、实际模型；
- 最近一次咨询的结果与耗时；
- 主要动作：“测试咨询”；
- 错误时只给一个明确的恢复动作。

#### 协作

- 征询策略四档选择；
- Assist / Outsource / Review 三段式模式选择；
- 各模式回合预算；
- 提示：模式变更只影响下一次咨询。

#### 浏览器与模型

- Edge 远程调试状态；
- 发现并绑定 Copilot 标签页；
- 当前登录状态与目标 URL；
- 允许模型的拖拽优先级；
- 菜单最短/最长等待和回复超时；
- 高级参数默认折叠。

#### 咨询记录

- 时间、来源、协作模式、实际模型、状态、耗时；
- 打开原 Copilot conversation；
- 不默认保存 prompt 与回复正文。

#### 设置

- 作为与概览、历史对话、协作默认设置、浏览器与模型并列的一级版块；
- 显示语言：中文 / English，切换后立即更新应用界面并原子持久化到本机设置；
- 只翻译 Bridge 的界面文案、状态和提示，不翻译或改写用户项目名、Markdown 会话和 Copilot 正文；
- 开机启动暂不提供；
- 日志级别和保留天数；
- 导出脱敏诊断包；
- 版本、Plugin/MCP 状态；
- 重置绑定与恢复默认设置。

### 13.3 GUI 行为边界

- 正常最小化由 Windows 管理，v1 不做系统托盘常驻程序。
- 关闭 GUI 不影响已经由 Codex 启动的 MCP 进程。
- GUI 与 MCP 同时发起咨询时，后发者立即显示“另一项咨询正在进行”，不排队。
- 不向普通用户展示 CSS selector、CDP 端口、内部 target ID 或原始协议消息。

## 14. 配置、状态与日志

### 14.1 本地目录

```text
%LOCALAPPDATA%\CopilotBridge\
  config.json
  state.json
  logs\
```

### 14.2 `config.json`

建议首版结构：

```json
{
  "schemaVersion": 1,
  "invocationPolicy": "manual",
  "collaborationMode": "assist",
  "modelPriority": [
    "opus",
    "gpt_5_6_think_deeper",
    "deep_thinking"
  ],
  "menuMinimumWaitMs": 2000,
  "menuMaximumWaitMs": 6000,
  "menuPollMs": 250,
  "replyTimeoutSeconds": 300,
  "assistMaxTurns": 2,
  "outsourceMaxTurns": 6,
  "reviewerCount": 2,
  "storeConversationContent": false,
  "keepMcpRunningInBackground": true,
  "logLevel": "Information",
  "logRetentionDays": 7
}
```

GUI 只允许在合理范围内修改数值。模型队列只能包含三种允许模型，不能把“自动”或快速模式重新加回去。

### 14.3 `state.json`

只保存最小运行元数据：

- 最近绑定和健康检查时间；
- consultation ID 到 Copilot conversation URL 的映射；
- 当前模式和实际模型；
- 最后结果状态；
- 不保存 cookie、令牌、prompt 或完整回复。

配置和状态通过临时文件 + 原子替换写入。首版只有 `schemaVersion: 1` 和直接读取逻辑，不建立通用迁移框架；真实出现第二版格式时再写一次具体迁移。

### 14.4 日志

默认记录：

- 连接、绑定、模型选择与状态转换；
- 选择器命中类型，不记录完整页面 HTML；
- 发送前/后边界、耗时和错误码；
- prompt/reply 只记录长度和 SHA-256 摘要，不记录正文。

诊断导出可以包含经过裁剪的目标 DOM 结构，但必须由用户在 GUI 中显式触发。

## 15. 错误模型

稳定错误码保持少而明确：

| 错误码 | 是否已发送 | 可否安全重试 | 用户动作 |
|---|---:|---:|---|
| `edge_not_running` | 否 | 是 | 启动 Edge |
| `remote_debugging_disabled` | 否 | 是 | 按向导启用远程调试 |
| `tab_rebind_required` | 否 | 是 | 在 GUI 重新绑定 Copilot 标签页 |
| `authentication_required` | 否 | 是 | 在 Edge 标签页完成登录/MFA |
| `invalid_target_page` | 否 | 是 | 检查目标 URL 后重新绑定 |
| `no_eligible_model` | 否 | 是 | 检查账号模型权限或菜单加载 |
| `composer_not_ready` | 否 | 是 | 刷新或检查页面状态 |
| `blocked_by_policy` | 否 | 否 | 修改 GUI 征询策略或管理员策略 |
| `busy` | 否 | 是 | 等待当前咨询结束 |
| `submission_unknown` | 未知 | 否 | 打开原会话人工确认，禁止自动重发 |
| `reply_timeout` | 是 | 否 | 打开原会话等待或读取结果 |
| `reply_extraction_failed` | 是 | 否 | 打开原会话，导出诊断 |

不实现通用重试中间件。只有明确处于发送前的连接/DOM 读取允许一次针对性恢复。

## 16. 项目结构

```text
Microsoft Copilot\
  CopilotBridge.sln
  PROJECT-DESIGN.md
  README.md                         # 实现开始后增加，只写安装和运行
  src\
    CopilotBridge\
      CopilotBridge.csproj          # 唯一生产项目
      App.xaml
      Program.cs
      UI\
        Views\
        ViewModels\
        Theme\
      Application\
        ConsultationCoordinator.cs
        Models\
      Infrastructure\
        Configuration\
        Edge\
        CopilotWeb\
        Mcp\
      Resources\
        m365-copilot-web.json
  tests\
    CopilotBridge.Tests\
      CopilotBridge.Tests.csproj    # 唯一测试项目
      Fixtures\
  plugin\
    .codex-plugin\
      plugin.json
    .mcp.json
    skills\
      copilot-consult\
        SKILL.md
    assets\
  docs\
    INSTALL.md
    TEAM-ROLLOUT.md
    TROUBLESHOOTING.md
```

不创建 `Core`、`Domain`、`Contracts`、`Adapters`、`WorkerRuntime` 等一组独立项目。命名空间足以表达内部边界。

## 17. Frozen 项目迁移边界

Frozen ChatGPT 项目继续保持冻结，绝不引用其 `.csproj`、DLL 或运行时组件。

只允许重新实现以下经过验证仍有价值的思路：

| 可迁移思路 | 新项目做法 |
|---|---|
| M365 Copilot host/path 与模型别名 | 压缩为一个内嵌的 `m365-copilot-web.json` |
| 渲染 DOM → Markdown | 在单一 Page Driver 中重新实现并用 fixtures 测试 |
| Copilot turn DOM 识别 | 只保留新 user/assistant turn 和生成状态判断 |
| STDIO MCP host | 使用当前官方 C# MCP SDK 重新实现两个工具 |

明确丢弃：

- Control/Capture 多版本协议；
- Worker、CLI、多个可执行文件；
- Admission/inbox/任务认领；
- 网络捕获和一次性 HTTP 转发门；
- 自定义传输记录、证据封存和哈希闭环；
- 多 Provider 和兼容性框架；
- 未被首个真实闭环证明必要的恢复机制。

旧项目只作为经验与反例，不能成为新项目依赖。

## 18. 测试策略

### 18.1 单元测试

- 模型别名标准化和禁用列表；
- Opus → GPT 5.6 → 深度思考优先级；
- 征询策略对 `trigger` 的允许/拒绝；
- 配置范围验证和原子写入；
- 发送前/发送后错误到 `canRetrySafely` 的映射；
- consultation ID 复用规则。

### 18.2 DOM fixture 测试

至少覆盖：

1. 初始只有自动、快速答复、深度思考，2.5 秒后出现 Opus/GPT。
2. Opus 可见但禁用，回退 GPT 5.6。
3. GPT 子菜单同时出现 5.6 与 5.5，必须选择 5.6。
4. 前两者不可用，选择深度思考。
5. 只有禁用模型，发送零次。
6. assistant reply 生成中、完成、超时和错误。
7. Markdown 中的代码块、列表、表格与链接。

### 18.3 实页验收

实页测试逐级进行，不以大量离线测试替代真实闭环：

| Gate | 操作 | 通过条件 |
|---|---|---|
| G1 | 连接日常 Edge、读取目标页状态 | 不切前台、不访问其他标签页内容 |
| G2 | 后台打开模型菜单并选择 Opus | 等待水合后准确选择并读回 |
| G3 | 用户点击 GUI“测试咨询”发送一条无害消息 | 只发送一次并读取完整回复 |
| G4 | Codex 通过 MCP 执行 Assist | 返回结构化结果并继续原任务 |
| G5 | 连续执行 10 次咨询 | 零重复发送、零前台抢占 |
| G6 | 用户同时操作鼠标、键盘和其他 Edge 标签页 | 当前窗口、当前标签和光标均不被改变 |
| G7 | 测试 Edge 重启、登录失效、模型回退 | 返回预期错误或回退结果 |
| G8 | 本机隔离分发验收 + 真实 Edge 闭环 | 隔离完成安装、Plugin/MCP 启动、卸载与宿主配置保护；完成浏览器授权后的真实日常 Edge 完成后台 Assist 且不抢前台 |

在用户没有明确触发测试咨询前，开发过程不应自行向真实 Copilot 发送消息。

## 19. 开发里程碑与硬门槛

各阶段的 Goal 文本、自动执行授权、时间预算、状态记录和停止条件见 [EXECUTION-ROADMAP.md](./EXECUTION-ROADMAP.md)。本节保留总体里程碑定义。

### Phase 0：设计与仓库基线

交付：

- 本设计文档；
- 初始化 Git；
- 创建一个生产项目和一个测试项目；
- 写入最小 `.gitignore`。

通过条件：目录结构与复杂度预算一致。不得提前生成 GUI 页面、MCP 工具和抽象框架。

### Phase 1：Edge/CDP 功能探针

在同一个生产项目中实现 `--probe`：

- 连接日常 Edge；
- 绑定已打开的 Copilot 标签页；
- 在同一 browser context 中后台重建一次测试标签页，并确认未切换前台；
- 读取当前模型；
- 后台打开菜单并执行模型优先级；
- 在用户点击确认的探针命令中发送一条测试消息并读取回复。

通过条件：G1–G3 全部通过。若无法做到无前台抢占，暂停项目并重新评估技术路线，不开始 GUI。

### Phase 2：最小业务核心

交付：

- Settings Store；
- Edge Session Adapter；
- Copilot Page Driver；
- Consultation Coordinator 的 Assist；
- 单元与 DOM fixture 测试。

通过条件：发送前/后边界和所有模型回退 fixtures 通过；生产代码累计不超过约 2500 行。

### Phase 3：薄 GUI 纵切

交付：

- 概览；
- 协作模式/征询策略设置；
- 浏览器绑定与模型设置；
- 测试咨询；
- 基础 UniFi/Apple 主题。

通过条件：非开发人员可以不看日志完成首次绑定和一次测试咨询。此阶段不做动效精修、托盘或安装器。

### Phase 4：Codex MCP + Skill

交付：

- `--mcp` STDIO 模式；
- 两个 MCP 工具；
- `copilot-consult` Skill；
- 项目级本地 MCP 配置。

通过条件：G4–G6 通过；逐工具预授权实际生效；任何不确定发送都不重试。

### Phase 5：完整三模式

依次加入：

1. Outsource 多轮复用；
2. Review 的两个隔离会话；
3. 最小咨询记录。

通过条件：三种模式均由 GUI 手动选择，MCP schema 中仍不存在 `mode`，Codex 能正确裁决 Review 分歧。

### Phase 6：团队分发

交付：

- 稳定路径的 Windows 安装包；
- `.codex-plugin/plugin.json`、`.mcp.json` 与 Skill；
- 安装、团队部署和故障排查文档；
- 本机隔离环境分发验收与真实日常 Edge 后台咨询。

通过条件：G7–G8 通过。不同硬件、账号和企业策略环境的团队试点保留为 v1 后兼容性验证，不作为本地团队 v1 门禁。先完成团队内部安装，不提前建设公共市场、自动更新服务或遥测后台。

## 20. 反过度工程化预算

以下不是建议，而是 v1 的硬限制：

- 生产项目最多 1 个，测试项目最多 1 个。
- 生产可执行文件只有 `CopilotBridge.exe`。
- 首个真实闭环前生产代码目标不超过 2500 行；完整 v1 目标不超过 7000 行，生成代码与测试除外。
- 直接生产 NuGet 依赖原则上不超过 5 个。
- 不创建数据库、本地 Web server、后台 daemon、消息队列或自定义 RPC。
- 不为尚未存在的第二个 Provider 建抽象。
- 不为尚未出现的配置版本建迁移框架。
- 不为理论上的浏览器故障叠加第二套自动化技术。
- 不用测试数量代替真实网页闭环。
- 新功能只有在出现真实用户案例、可复现故障或明确团队需求后才进入设计。

每个里程碑结束时删除未使用的抽象和代码；“未来可能有用”不是保留理由。

## 21. 主要风险与应对

| 风险 | 应对 |
|---|---|
| Microsoft 365 Copilot DOM 变化 | 小型 provider 资源 + 精确可访问性定位 + fixture + 清晰错误，不增加 OCR 兜底 |
| Edge CDP 对日常配置档兼容性不足 | Phase 1 先做真实探针；失败就停止，而不是并行维护两套实现 |
| 后台新建标签页仍抢前台 | 只用 `Target.createTarget(background=true)` 且经过实机门禁；失败则禁用自动重建 |
| 模型菜单延迟水合 | 2 秒最短等待、稳定快照和 6 秒总窗口 |
| 点击发送后网络状态不明 | `submission_unknown`，禁止重发，返回原会话 URL |
| 企业策略强制 MCP 审批 | 检测并显示，不能绕过；由管理员调整策略 |
| 多个 Codex 任务同时调用 | 单写入互斥锁，后发立即 `busy`，不实现队列 |
| Review 变慢或消耗过多额度 | 每次请求保持两个 reviewer 串行，用户可停止继续追问或在 GUI 切回 Assist |
| 团队 Edge 配置档不同 | 一次性显式绑定，不猜测账号或 Profile |

## 22. 发布定义

### 首个可用版本（0.1）

仅包含：日常 Edge 绑定、后台模型选择、一次 Assist 发送/读取和基础 GUI。它用于证明技术路线，不对外分发。

### 团队 v1

包含：

- 三种手动协作模式；
- 四档征询策略；
- 模型优先级与延迟水合；
- 两个 MCP 工具与 Skill；
- GUI 设置、诊断和最小历史；
- Windows 安装与 Codex Plugin；
- G1–G8 全部通过。

这里的 G8 是本机隔离分发与真实 Edge 闭环门禁，不代表已经覆盖所有团队硬件、Windows build、账号或企业策略组合。

### v1 之后才评估

- 自动选择或升级协作模式；
- 文件和图片附件；
- 系统托盘与开机启动；
- Edge 重启后的自动跨 Profile 恢复；
- Review 的指定模型矩阵；
- 集中团队配置与自动更新；
- 其他 Copilot 页面或其他模型服务。

这些项目不进入当前代码，只有在真实使用证明必要后另立设计。

## 23. 设计依据

- OpenAI：MCP 连接外部系统，Skill 定义可复用工作流，Plugin 负责团队分发。
- OpenAI：Codex 桌面、CLI 和 IDE 扩展共享本地 MCP 配置；本项目使用 STDIO server。
- OpenAI：写工具必须使用准确的安全注解，逐工具审批可以单独配置。
- Microsoft Edge：通过 DevTools Protocol 连接正在运行的 Edge，并可使用日常 user-data 目录。
- Playwright .NET：Chromium 可通过 CDP 接入现有浏览器实例。

当前参考：

- https://learn.chatgpt.com/docs/extend/mcp
- https://learn.chatgpt.com/docs/build-plugins
- https://learn.chatgpt.com/docs/agent-approvals-security
- https://learn.microsoft.com/en-us/microsoft-edge/web-platform/devtools-mcp-server
- https://learn.microsoft.com/en-us/microsoft-edge/devtools/protocol/
- https://chromedevtools.github.io/devtools-protocol/tot/Target/#method-createTarget
- https://playwright.dev/dotnet/docs/api/class-browsertype

## 24. 下一步唯一入口

本设计确认后，只执行 **Phase 0 → Phase 1**：初始化最小仓库，并用同一个生产项目完成 Edge/CDP 探针。

在 G1–G3 通过以前，不开始完整 GUI、不创建 Plugin、不实现三种模式，也不从 Frozen 项目复制代码。首个要证明的问题只有一个：

> 能否在用户继续正常使用电脑和 Edge 的同时，后台准确选择 Opus、发送一次消息并读回回复，且完全不抢前台、不重复发送？

## 25. v1.1：本地会话工作台

v1 已完成其“安全后台征询”目标。v1.1 不改变 CDP/DOM、单生产项目、单写入互斥锁和不重试发送的边界；它把 Bridge 管理的即时咨询变成用户可见、可分类、可检索的本地 Markdown 会话资产。

### 25.1 第一阶段范围

- 新的即时咨询在本地工作区保存完整 Markdown：发送内容、回复内容、时间、角色、实际模型和模型验证状态。
- 每个会话使用稳定的本地会话 ID 与 Copilot conversation URL 关联；标题绝不作为关联键。
- 保存 Copilot 初始标题、当前标题、标题历史和用户本地标题。用户本地重命名后只改变显示标题，不覆盖网页标题记录。
- 对话管理页升级为项目、会话列表和单会话详情的工作台；支持创建项目、把会话移动到项目文件夹、编辑本地标题、查看 Markdown 与会话内检索。
- 默认项目层级只有受保护的“未分类对话”和用户创建的项目。旧“收件箱”“独立对话”会在启动时原子迁移到“未分类对话”。一个会话只有一个主项目；多项目引用留待后续设计，不能复制正文产生分叉。
- 提供复制当前 Markdown 的 UI 动作；通用 Agent 历史读取接口不在本阶段实现。

### 25.2 明确延后

- 把整项目内容自动发送给任一 Agent。后续只能提供项目概览、检索片段和经用户确认的完整范围。
- 通用 Agent 中台的列表、读取和检索 MCP 工具。必须等 Markdown 文件格式和项目调用范围稳定后另行设计。
- 文件、图片和仓库批量上传。

### 25.3 本地存储与信任边界

工作区由用户选择；默认位于 `%LOCALAPPDATA%\CopilotBridge\workspace`。会话正文是用户明确启用即时记录后写入的 Markdown，Bridge 不导入旧网页线程，也不读取其他 Edge 标签页正文。每个 Copilot 回复仅在 Bridge 当次实际读回模型时标记为“已验证”；历史或无法证明的模型必须标记为“未知”。

### 25.4 v1.1 第一阶段验收

用户不看日志即可：创建项目；发起一次即时咨询；在项目中看到完整 Markdown 会话；修改本地标题但仍看到 Copilot 标题；移动会话到另一个项目；以关键词命中发送和回复内容；复制当前会话 Markdown。现有 G1–G8 回归继续通过，且不会因新增记录改变发送次数或前台占用。

### 25.5 自适应状态刷新

概览页保留“刷新状态”作为立即检查入口，同时自动读取已绑定页面的连接、登录和当前模型状态。读取只使用既有 CDP 会话与 DOM 控件，不打开模型菜单、不读取对话正文、不发送消息。

- 概览页处于前台时，每 10 秒检查一次。
- 切换到其他页面、窗口失焦或最小化时，每 60 秒检查一次。
- 发生连续失败时按 30、60、120 秒退避，成功后恢复正常间隔。
- 正在咨询或已有刷新进行时跳过本轮，绝不并发建立第二个刷新流程。
- UI 显示上次检查时间和下一次预计检查；自动失败不反复弹出错误提示，手动刷新保留完整错误说明。

“页面当前模型”只描述此刻 Copilot 页面显示的模型；下一次咨询仍按用户配置的模型优先级执行，二者在页面被用户手动切换时可以不同。

### 25.6 显示语言

主导航新增一级“设置”。设置页提供中文与 English 两种显示语言，选择后立即应用，并随 `settings.json` 原子写入而持久化。语言切换不读取、翻译、迁移或修改用户的本地 Markdown、项目名称、Copilot 标题及对话正文；它只影响 Bridge 自身界面。

### 25.7 显式旧对话导入

历史模式只允许用户主动点击“导入当前旧对话”。Bridge 必须在写入前展示当前 Copilot 标题、conversation URL、用户/Copilot 消息数和明确的本地留存提示；用户确认前不创建任何 Markdown 文件。

- 只读取当前唯一、已绑定 Copilot 标签页的 DOM 已加载消息，不扫描侧栏、不枚举其他网页标签，也不滚动、导航或发送消息。
- 每个导入会话以 Copilot conversation URL 去重；已导入时不写入第二份 Markdown。
- 网页标题存为 Copilot 初始/当前标题，本地改名仍具有显示优先级。
- 旧回复没有可验证的逐条模型来源，必须写为“未知”，绝不根据当前页面模型反推。
- 长对话若网页采用虚拟列表，导入预览只报告“已加载消息数”，不宣称已导出完整网页历史。

### 25.8 v1.1.2：对话管理起点

v1.1.2 先集中改进对话管理的信息架构，不提前实现双标题同步、会话级模型控制、项目级受控调用或内容交接。

- 一级导航与页面标题统一从“历史对话”更名为“对话管理”。
- 项目作为对话管理内独立、有标题的板块；项目文件夹使用与模型优先级一致的卡片式选项视觉，而不是无标题的普通列表。
- 仅自定义项目可通过右键菜单“置顶/取消置顶”；置顶状态写入项目目录的本地标记，重启后仍保留。系统项目保持受保护，不提供置顶、改名或删除。
- 排序固定为系统项目在前，其后是置顶的自定义项目，再按项目名排序；不把置顶变成项目内容复制、跨项目引用或新的 Agent 读取权限。

验收：中文与 English 界面均显示“对话管理 / Conversation Management”；项目板块有明确标题和项目文件夹说明；自定义项目可置顶、取消置顶、重启后状态不丢失且改名后保留；系统项目的保护规则不回归；不改变 Copilot 页面、MCP 工具面、会话发送次数或本地 Markdown 正文。

### 25.9 v1.1.2：MCP 后台常驻

设置页提供“后台常驻”选项，默认开启，以兼容既有“GUI 关闭后 MCP 可继续服务 Codex”的行为。

- MCP 启动时仅在本地状态文件登记自己的 PID、可执行文件路径与启动时间；正常退出时删除登记，异常退出留下的失效登记会在下一次读写时清理。它不是端口、后台服务或通用 RPC。
- GUI 关闭时只处理已登记且可执行文件路径与 GUI 自身一致的 MCP 进程，绝不按进程名称终止其他版本或其他应用。
- 关闭“后台常驻”时，关闭 GUI 会直接终止已登记的 MCP 进程；开启时，关闭 GUI 必须询问用户是否终止，选择“否”只关闭 GUI，选择“是”同时终止 MCP。
- 终止 MCP 可能中断正在进行的咨询；该选择由关闭 GUI 的用户确认，不触发额外 Copilot 发送或重试。

验收：设置原子持久化并在重启后恢复；MCP 的登记在启动/正常退出后正确写入/清理；关闭 GUI 只会针对精确登记的同路径 MCP 执行用户选择，不影响未登记旧版本、其他应用或 Copilot 页面。

### 25.10 v1.1.2：Copilot 风格、快捷方式与对话编排

在不扩大 MCP、浏览器和存储边界的前提下，统一设置页与对话管理页的 Microsoft Copilot 视觉语言，并补齐桌面入口和本地排序体验。

- 设置页新增“快捷方式”，提供固定到任务栏、固定到“开始”和创建桌面快捷方式；Windows 未公开自动固定动作时，只创建或定位快捷方式并明确提示用户手动确认，不伪报成功。
- “收件箱”和“独立对话”统一迁移为“未分类对话”；“未分类对话”是唯一系统项目，固定在项目列表首位，不允许删除、重命名、置顶或拖拽调整。显式导入的网页对话始终写入“未分类对话”。
- 自定义项目卡片显示拖拽手柄；系统项目显示锁定图标。自定义项目排序写入现有项目标记文件，不增加数据库或新服务。
- 项目、模型和对话拖拽统一使用轻量透明度、缩放和位移动效；不引入第二套拖拽框架。
- UI 采用中性浅色/深色画布、灰色选择态、圆角设置卡片、`Noto Sans SC` 中文字体和 `Segoe Fluent Icons`，不改写用户项目名、对话内容或 Copilot 标题。
- 沟通轮次不设应用级上限；旧设置中的 `conversationTurnLimit` 在首次读取时完成原子迁移并立即移除。设置页不再显示 1–20 输入。
- 保存和发送等主要操作统一放在所属卡片的右上角操作位，与卡片标题保持 24 px 水平间距；路径浏览等字段级操作继续紧邻对应输入框。

验收：迁移、系统项目保护、排序持久化与快捷方式创建由自动化测试覆盖；Debug 构建和测试通过；视觉验收必须区分已观察状态与未观察状态，不能用代码存在替代实机动效结论。

### 25.11 v1.1.2：继续开发范围

WorkBuddy/GLM-5.2 后续讨论已按复杂度分割。v1.1.2 只继续完成不改变 MCP、会话存储和进程架构的任务；详细范围以 [v1.1.2-followup-design.md](./v1.1.2-followup-design.md) 为准。

- 先完成 Phase 13 的浅色、深色、拖拽、快捷方式和卡片操作位视觉门禁。
- 自动概览刷新改为静默读取，不再周期性调用全局忙碌状态；保留旧值并只更新变化字段。
- 将即时咨询主要入口移至概览，并增加打开当前工作区的文件管理器入口；两者复用既有业务能力。
- 正式发布物必须真实携带并验证 Noto Sans SC 及其再分发许可，不能依赖开发机字体后宣称替换完成。
- 完成构建、测试、1.1.1 升级、安装/卸载和视觉候选验收后才允许发布 1.1.2。

本阶段不修改 Markdown 格式或文件名，不增加系统托盘、开机启动、多调用方适配、Adaptive、内容交接、全局热键或完整 UI 资源重构。沟通轮次继续无应用级上限。

## 26. v1.2.0 设计边界

v1.2.0 的范围已经由用户确认并启动，详细设计以 [v1.2.0-design.md](./v1.2.0-design.md) 为准。该版本不重写已通过 G1–G8 的 Edge/CDP/DOM 发送机制，而是让 Codex 能在用户按项目授权的范围内检索、读取和复用本地 Copilot Markdown 会话。

### 26.1 核心增量

- 在每个本地项目现有标记中保存 `off`、`metadata`、`snippets` 或 `full` 四级访问权限；默认和升级结果始终为 `off`。
- MCP 新增 `search_conversations` 与 `read_conversation` 两个只读、非破坏、非 open-world 工具。
- Codex 先检索、再读取一个明确会话的必要 turn 范围，并自行组织 `requestMarkdown`；Bridge 不隐式把历史正文拼入外部发送。
- 现有 `consult_copilot` 的 schema、GUI 模式控制、单写入锁、发送状态和禁止自动重发语义保持不变。

### 26.2 继续延期

会话存储 v2、系统托盘、开机启动、完整 UI 设计系统、Adaptive、双标题自动同步、会话级模型控制、多调用方框架和第三个写工具不进入 v1.2.0。WorkBuddy 继续只作为开发期草稿工具，不进入产品运行时或发布物。
