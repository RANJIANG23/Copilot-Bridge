# Copilot Bridge 目标模式执行路线图

> 版本：v1.1
> 日期：2026-07-19（Asia/Shanghai）
> 适用目录：本仓库根目录
> 上位设计：[PROJECT-DESIGN.md](./PROJECT-DESIGN.md)
> 当前状态：Phase 0–6 与修订后的 G1–G8 已通过；v1.1 Phase 7–10 已通过

## 1. 文档用途

本文档把 Copilot Bridge 的 Phase 0–6 转换为可执行、可验证、可直接用于 Codex 目标模式的目标序列。

它解决四个问题：

1. 让 Codex 在用户不持续介入的情况下自主推进。
2. 让每个阶段有明确终点，避免把“持续推进”理解为无限扩建。
3. 让 Computer Use 只替代必要的人机界面操作，而不成为日常自动化技术路线。
4. 让任何阶段失败时停在原地，不通过增加第二套架构绕过失败。

本路线图不替代总体设计。发生冲突时，以 `PROJECT-DESIGN.md` 中的产品边界、非目标和复杂度预算为准。

## 2. 推荐运行配置

- 模型：**GPT-5.6 Sol**。
- 推理强度：**High**。
- 模式：**Goal mode**。
- Ultra：默认关闭。
- 子 Agent：默认不使用；只有用户后续明确授权某个可并行子任务时才使用。
- 工作方式：一个阶段通过后再进入下一阶段。

如某个边界明确的技术难题在 High 下连续两轮仍无法定位，可以临时提高到 Extra High；单个最难问题可以临时使用 Max。问题解决后恢复 High，不把更高推理强度作为项目永久默认值。

## 3. 目标模式使用方式

### 3.1 推荐方式：一个总 Goal，内部严格分阶段

用户可以在 Goal mode 中引用本文档并要求按顺序推进。Codex可以自动从一个已通过阶段进入下一个阶段，但必须：

1. 完成本阶段全部交付物。
2. 执行本阶段验证。
3. 确认本阶段停止条件没有触发。
4. 更新本文档的状态记录。
5. 创建本阶段本地 Git 提交。
6. 才能进入下一阶段。

任何门禁失败都必须停止自动升级，记录证据并报告具体阻塞。不得把“下一阶段也许能解决”作为继续的理由。

### 3.2 更保守方式：每个阶段单独建立 Goal

如果希望在阶段之间人工复核，可复制本文档每个阶段末尾的 Goal 文本分别启动。阶段目标已经包含结果、约束、验证与停止条件。

两种方式的技术实施内容完全一致，区别只在阶段之间是否等待用户确认。

## 4. 全局执行规则

以下规则适用于 Phase 0–6，每个阶段自动继承。

### 4.1 自动执行授权

在项目目录和本项目明确涉及的本机环境内，允许 Codex自行进行：

- 读取、搜索和编辑本项目文件；
- 初始化本地 Git、创建本地提交和查看历史；
- 创建、构建、运行和测试本项目；
- 安装本项目所需且符合依赖预算的 NuGet 包；
- 执行非破坏性的 PowerShell、`dotnet`、Git、Edge/CDP 和测试命令；
- 启动、停止和调试本项目产生的进程；
- 使用 Computer Use 完成必要的 Windows、Edge、Copilot 或本项目 GUI 操作；
- 向用户自己的 Microsoft 365 Copilot 发送与本项目验证直接相关的无害测试 prompt；
- 读取这些测试 prompt 的回复；
- 在失败后执行明确处于“发送前”的一次安全恢复。

上述操作不需要逐项询问用户。

### 4.2 仍须停止的边界

以下情况不能凭“自主推进”自行扩大权限：

- 需要用户本人完成 MFA、CAPTCHA、硬件密钥或管理员凭据输入；
- Windows 安全桌面或企业策略阻止自动操作；
- 需要购买服务、证书或订阅；
- 需要向 Teams、邮件、SharePoint、GitHub 远程仓库或其他人员发送/发布内容；
- 需要访问尚未授权的外部团队电脑或账号；
- 需要改变既定架构、增加第二套自动化或突破代码规模预算；
- 需要执行不可逆的破坏性系统操作；
- 需要恢复或修改 Frozen ChatGPT 项目。

遇到这些情况时，先保存当前状态、记录已完成验证，再明确请求用户处理唯一阻塞项。

### 4.3 Computer Use 使用边界

Computer Use 是开发和验收辅助，不是产品的日常自动化实现。

优先级固定为：

1. 文件和命令行工具；
2. Edge CDP/DOM；
3. Computer Use。

Computer Use 可以用于：

- 在 Edge 中启用远程调试；
- 处理一次性登录或页面设置；
- 选择、观察和验证真实 GUI 状态；
- 操作 Copilot Bridge 自己的 WPF 界面；
- 在无前台抢占测试中扮演“用户正在使用其他窗口”的测试角色。

Computer Use 不得被写入产品运行时，不得成为 CDP/DOM 失败后的生产兜底。

### 4.4 Git 与阶段记录

- Phase 0 初始化 Git，但不配置或推送远程仓库。
- 每个阶段通过后创建一个本地提交，例如 `phase 1: prove background Edge loop`。
- 不为每个小步骤频繁提交；每阶段一个可恢复基线即可。
- 不使用 `git reset --hard` 或覆盖用户已有修改。
- 在本文档状态表中记录阶段状态、提交 ID 或稳定阶段标签、实耗时间和主要验证结果。

### 4.5 反过度工程化规则

- 生产项目最多 1 个，测试项目最多 1 个。
- 生产可执行文件只有 `CopilotBridge.exe`。
- 首个真实闭环前生产代码不超过约 2500 行。
- 完整 v1 生产代码目标不超过约 7000 行。
- 直接生产 NuGet 依赖原则上不超过 5 个。
- 不创建数据库、本地 Web server、后台 daemon、消息队列、自定义 RPC 或多 Provider 框架。
- 不实现当前阶段没有要求的后续功能。
- 不以“未来可能有用”为理由保留抽象、兼容层或备用实现。
- 阶段超出时间上限时先判断路线是否错误，不能用增加组件来掩盖问题。

## 5. 阶段总览

| 阶段 | 主题 | 连续工作预算 | 主要门禁 | 当前状态 |
|---|---|---:|---|---|
| Phase 0 | 设计与仓库基线 | 1–2 小时 | 最小结构与复杂度预算 | 通过 |
| Phase 1 | Edge/CDP 功能探针 | 6–10 小时 | G1–G3 | 通过 |
| Phase 2 | 最小业务核心 | 6–9 小时 | fixtures、发送边界、≤2500 LOC | 通过 |
| Phase 3 | 薄 GUI 纵切 | 8–12 小时 | 非开发人员可完成首次使用 | 通过 |
| Phase 4 | Codex MCP + Skill | 4–7 小时 | G4–G6 | 通过 |
| Phase 5 | 完整三模式 | 7–11 小时 | 手动模式与会话隔离 | 通过 |
| Phase 6 | 团队分发 | 6–10 小时 | G7–G8 | 通过 |
| **总计** | **团队 v1** | **38–61 小时** | **全部门禁通过** | **内部团队 v1 候选版完成** |
| Phase 7 | v1.1 会话工作台纵切 | 8–12 小时 | 即时 Markdown、项目归类、会话检索 | 通过 |
| Phase 8 | 自适应状态刷新 | 2–3 小时 | 前后台节流、退避与状态可见性 | 通过 |
| Phase 9 | 设置与显示语言 | 1 小时 | 一级设置页、中文/English 切换与原子持久化 | 通过 |
| Phase 10 | 显式旧对话导入 | 2–4 小时 | 当前对话预览、确认导入、Markdown 留存与去重 | 通过 |

时间预算是路线健康检查，不是要求为了赶时间跳过验证。超过单阶段上限且没有接近门禁时，应停止并报告路线问题。

## 6. 总 Goal 启动文本

如果用户希望最大程度无人值守地连续推进，可以在 Goal mode 中使用以下目标：

```text
在本仓库根目录中，完整阅读并严格遵守
PROJECT-DESIGN.md 与 EXECUTION-ROADMAP.md。

按照 EXECUTION-ROADMAP.md 的 Phase 0、1、2、3、4、5、6 顺序自主推进
Copilot Bridge，直到团队 v1 完成，或遇到文档规定的真实阻塞。

每个阶段必须先完成交付物、通过门禁、更新路线图状态并创建本地 Git
阶段提交，之后才能进入下一阶段。不得提前实现后续阶段，不得通过增加
第二套架构绕过当前门禁。

允许自行执行项目内文件修改、依赖安装、构建、测试、进程调试、Edge/CDP
操作，以及使用 Computer Use 完成必要的 Windows、Edge、Copilot 和本项目
GUI 操作。允许向我自己的 Microsoft 365 Copilot 发送本路线图所需的无害
测试消息并读取回复，无需逐条确认。

Computer Use 只作为界面操作和验收辅助，不能成为产品运行时自动化方案。
不得恢复 ChatGPT Frozen 项目，不得向邮件、Teams、SharePoint、GitHub 远程
仓库或其他人员发布内容，不得购买服务或绕过企业策略。

默认使用 Sol + High，不使用 Ultra 或子 Agent。只有遇到需要本人完成的
MFA/CAPTCHA/管理员凭据、需要改变既定架构，或当前
阶段门禁无法通过时，才暂停并向我请求一个具体操作。

全部 G1–G8 及各阶段补充门禁通过后才可将目标标记为完成。G8 使用本机
隔离分发环境和本机真实日常 Edge；不同硬件、账号与企业策略环境留作 v1
后的团队兼容性试点，不得把本机结果表述为跨设备证明。
```

## 7. Phase 0：设计与仓库基线

### 7.1 目标

把空目录转换为最小、可构建、可测试、可回退的 .NET 仓库，同时不提前实现业务功能。

### 7.2 当前结果

- `PROJECT-DESIGN.md` 已完成。
- `EXECUTION-ROADMAP.md` 已完成。
- 本地 Git 与 `CopilotBridge.sln` 已建立。
- 唯一生产项目和唯一测试项目均以 `net10.0-windows` 构建通过。
- Phase 1 尚未开始。

### 7.3 范围内工作

- 初始化本地 Git。
- 创建 `CopilotBridge.sln`。
- 创建唯一生产项目 `src/CopilotBridge/CopilotBridge.csproj`。
- 创建唯一测试项目 `tests/CopilotBridge.Tests/CopilotBridge.Tests.csproj`。
- 目标框架使用 `net10.0-windows`。
- 生产项目初始保持最小可执行结构，为 Phase 1 的 `--probe` 服务。
- 添加最小 `.gitignore`、`README.md` 和项目级 `AGENTS.md`。
- `AGENTS.md` 只固化当前阶段、验证命令、复杂度预算和禁止提前开发规则。

### 7.4 明确不做

- 不添加 WPF 页面。
- 不添加 Playwright 之外的预备浏览器框架。
- 不添加 MCP SDK、Plugin、Skill 或安装器。
- 不创建 Core/Domain/Contracts/Adapters 等额外项目。
- 不复制 Frozen 源码。

### 7.5 验收

- `dotnet build` 成功。
- 空测试项目可以运行。
- 仓库只有一个生产项目和一个测试项目。
- 没有业务占位层、无用接口或预建目录树。
- 本地提交：`phase 0: establish minimal repository baseline`。

### 7.6 停止条件

如果最小仓库无法在已安装的 .NET 10 SDK 下构建，先解决环境或项目类型问题，不进入 Phase 1。

### 7.7 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 0：设计与仓库基线。
只建立最小 Git/.NET 结构，不实现 Phase 1 及后续业务功能。
执行 Phase 0 验收，更新路线图状态并创建规定的本地阶段提交后停止。
```

## 8. Phase 1：Edge/CDP 功能探针

### 8.1 目标

用最少代码证明项目最关键的技术路线：在用户日常 Edge 的专用后台 Copilot 标签页中，准确选择允许模型、只发送一次测试消息并读回回复，而且不抢占前台。

### 8.2 范围内工作

- 在同一个生产项目中实现 `CopilotBridge.exe --probe`。
- 发现并连接日常 Edge 的 CDP 端点。
- 绑定用户已登录的 `m365.cloud.microsoft` Copilot 标签页。
- 记录当前 browser context 和 target。
- 验证 `Target.createTarget(background=true)` 在同一 context 中不会切换前台。
- 读取当前模型。
- 打开模型菜单，至少等待 2000 ms，并观察到菜单稳定或达到 6000 ms 上限。
- 按 Opus → GPT 5.6 Think deeper → 深度思考选择并读回验证。
- 禁止自动、快速答复和 GPT 5.5 快速响应。
- 使用无害测试 prompt，只提交一次并提取新增 assistant reply。
- 记录发送前/后边界；点击发送后不自动重试。

建议测试 prompt：

```text
这是 Copilot Bridge 的连接测试。请只回复：COPILOT_BRIDGE_TEST_OK
```

### 8.3 Computer Use 授权

允许 Codex 使用 Computer Use：

- 启用 Edge 当前实例的远程调试；
- 打开或登录用户自己的 Microsoft 365 Copilot；
- 选择用于一次性绑定的标签页；
- 观察当前窗口、标签页和光标是否被 Bridge 改变。

这些操作只用于开发设置和验证，探针本身必须使用 CDP/DOM。

### 8.4 G1–G3

- **G1**：连接日常 Edge 并读取目标页，不切换前台，不读取其他标签页正文。
- **G2**：等待菜单水合后选择最高可用允许模型，并读回验证。
- **G3**：只发送一次无害消息，等待完成并提取完整回复。

### 8.5 明确不做

- 不开始 WPF GUI。
- 不实现 MCP、Skill 或 Plugin。
- 不实现 Outsource/Review。
- 不加入 OCR、UIA、网络抓包或前台点击兜底。
- 不建立通用 selector/provider 框架。

### 8.6 验收

- G1–G3 全部通过。
- 前台窗口、用户当前 Edge 标签页和物理鼠标位置不因 CDP/DOM 操作改变。
- 测试消息在 Copilot 中只出现一次。
- 返回有效模型名称、conversation URL 和完整回复。
- 失败路径明确区分 `not_submitted` 与 `submission_unknown`。
- 本地提交：`phase 1: prove background Edge loop`。

### 8.7 停止条件

- 10 小时内无法稳定连接日常 Edge。
- 后台 target 或 DOM 操作仍会抢占前台。
- 无法可靠判断是否已经发送。
- 必须引入第二套自动化才能继续。

触发任一项时停止项目并报告，不能开始 Phase 2。

### 8.8 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 1：Edge/CDP 功能探针。
允许使用 Computer Use 完成一次性 Edge/Copilot 设置，并允许向我自己的
Microsoft 365 Copilot 发送一条规定的无害测试消息，无需再次确认。
探针运行时只能使用 CDP/DOM，不得使用前台鼠标键盘、OCR、UIA 或网络捕获。
只有 G1–G3 全部通过，更新路线图并创建阶段提交后才算完成；否则按停止条件
暂停，不进入 Phase 2。
```

## 9. Phase 2：最小业务核心

### 9.1 目标

把 Phase 1 探针整理为可测试的最小业务核心，不改变已经验证的浏览器技术路线。

### 9.2 交付物

- Settings Store。
- Edge Session Adapter。
- Copilot Page Driver。
- Consultation Coordinator 的 Assist 单次/追问流程。
- 最小 provider 资源 `m365-copilot-web.json`。
- xUnit 单元测试和静态 DOM fixtures。

### 9.3 必测场景

- 初始菜单只有三种粗粒度模式，延迟后出现 Opus/GPT。
- Opus 禁用后回退 GPT 5.6。
- GPT 5.6 与 GPT 5.5 同时出现，只能选 5.6。
- 前两者不可用时选择深度思考。
- 只有禁用模型时发送零次。
- 回复生成中、完成、超时和页面错误。
- Markdown 标题、列表、代码块、链接和简单表格。
- 点击发送前可以安全恢复；点击发送后禁止自动重试。

### 9.4 明确不做

- 不做 GUI、MCP、Plugin。
- 不做多会话调度或任务队列。
- 不做通用重试框架。
- 不保存完整 prompt/reply 历史。
- 不添加第二个 provider。

### 9.5 验收

- Phase 1 的真实闭环仍然通过。
- 所有模型、发送边界和 DOM fixtures 通过。
- Release build 成功。
- 生产代码累计不超过约 2500 行；超出时先删除不必要抽象。
- 本地提交：`phase 2: establish minimal consultation core`。

### 9.6 停止条件

如果整理核心导致 Phase 1 实页行为回退，先恢复最小闭环，不允许通过新增兼容层掩盖回归。

### 9.7 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 2：最小业务核心。
把已通过的探针整理为 Settings Store、Edge Session Adapter、Copilot Page
Driver 和 Assist Coordinator，并完成规定的单元与 DOM fixture 测试。
不得实现 GUI、MCP、Plugin、多 Provider 或通用恢复框架。
保持 Phase 1 实页闭环，生产代码累计不超过约 2500 行；通过验收、更新
路线图并创建阶段提交后停止。
```

## 10. Phase 3：薄 GUI 纵切

### 10.1 目标

在现有核心上增加一个可以真实使用的简洁 WPF 图形界面，形成 Windows 应用主体，但不提前进行视觉精修和团队打包。

### 10.2 交付物

- WPF 应用入口，与现有 `--probe` 共用一个生产项目。
- 概览页：Edge、标签页、登录、模型和最近咨询状态。
- 协作页：征询策略和 Assist/Outsource/Review 手动模式选择；未实现模式明确标记尚不可用。
- 浏览器与模型页：标签页绑定、允许模型队列、菜单等待和回复超时。
- 测试咨询入口。
- 最小咨询元数据展示。
- UniFi 信息层级 + Apple 式克制的基础主题。

### 10.3 GUI 边界

- 不展示 selector、CDP target ID 和原始协议。
- 不做系统托盘、开机启动、安装器、自动更新和完整动效。
- 不做卡片堆叠式通用管理后台。
- GUI 关闭不修改 Edge 或删除用户数据。

### 10.4 Computer Use 验收

允许 Codex 使用 Computer Use 作为非开发用户：

1. 启动 Copilot Bridge。
2. 完成标签页绑定。
3. 查看状态和模型队列。
4. 发起测试咨询。
5. 验证错误恢复提示。

Computer Use 不得被应用代码调用。

### 10.5 验收

- 不看日志即可完成首次绑定和一次测试咨询。
- 修改协作模式只影响下一次咨询。
- UI 响应期间不阻塞或抢占其他 Edge 标签页。
- 高 DPI 和常用 Windows 缩放下没有明显截断。
- Phase 1–2 测试继续通过。
- 本地提交：`phase 3: deliver thin Windows GUI`。

### 10.6 停止条件

GUI 不得重写 Page Driver 或产生第二套业务逻辑。如果为实现 UI 必须复制浏览器逻辑，应先修正边界再继续。

### 10.7 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 3：薄 GUI 纵切。
在同一个生产项目中增加最小 WPF GUI，并使用 Computer Use 从普通用户角度
完成绑定、设置和测试咨询验收。GUI 必须复用 Phase 2 核心，不复制浏览器
逻辑；不实现托盘、安装器、自动更新或视觉精修。
通过验收、更新路线图并创建阶段提交后停止。
```

## 11. Phase 4：Codex MCP + Skill

### 11.1 目标

把已经可用的 Windows 应用接入 Codex，使 Codex能够在任务中调用 Copilot，并确保每次外部发送的状态和审批语义准确。

### 11.2 交付物

- `CopilotBridge.exe --mcp` STDIO 模式。
- `copilot_bridge_status` 只读工具。
- `consult_copilot` 写工具。
- 工具准确的 read-only、destructive 和 open-world 注解。
- `copilot-consult` Skill。
- 项目级 `.codex/config.example.toml` 开发配置模板；实际 `.codex/config.toml` 仅保留在各自本机且不得提交。
- `consult_copilot` 的逐工具预授权配置。
- MCP 协议与进程退出测试。

### 11.3 工具边界

- 只暴露两个工具。
- `consult_copilot` 不接受 `mode` 或 `model` 参数。
- 协作模式和模型队列来自 GUI 配置。
- 追问复用 `consultationId`。
- `submission_unknown` 时 `canRetrySafely=false`。

### 11.4 G4–G6

- **G4**：Codex通过 MCP 执行 Assist，收到结构化回复并继续原任务。
- **G5**：连续 10 次咨询无重复发送、无错误串会话。
- **G6**：咨询运行时，由 Computer Use 操作另一个窗口或 Edge 标签页；记录前台窗口、活动标签和光标，确认 Bridge 没有抢占。

如果 Codex 需要重启才能加载 MCP，先保存工作状态并提交当前代码，再使用允许的应用控制完成重启或恢复；目标模式恢复后继续本阶段。

### 11.5 明确不做

- 不增加 list/cancel/job 等工具。
- 不做本地 HTTP MCP server。
- 不实现 Outsource 或 Review。
- 不自动路由协作模式。
- 不把写工具错误标记为只读以规避审批。

### 11.6 验收

- G4–G6 全部通过。
- Codex可以在 GUI 关闭时通过 `--mcp` 工作。
- GUI 与 MCP 并发写入时，后发调用立即返回 `busy`，不建立队列。
- 企业策略若强制审批，应用准确报告而不绕过。
- 本地提交：`phase 4: connect Codex through MCP and skill`。

### 11.7 停止条件

如果必须增加常驻服务、HTTP 端口、命名管道或第三个工具才能完成基本调用，应停止并重新检查实现，而不是扩大架构。

### 11.8 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 4：Codex MCP + Skill。
在同一个可执行文件中加入 STDIO MCP，只提供 status 与 consult 两个工具，
并创建 copilot-consult Skill 和项目级开发配置。consult 工具不得接收 mode
或 model，发送不确定时不得重试。
允许为加载 MCP 保存状态并重启/恢复 Codex。完成 G4–G6、更新路线图并创建
阶段提交后停止；不得提前实现 Outsource、Review 或 Plugin 分发。
```

## 12. Phase 5：完整三模式

### 12.1 目标

在不改变运输层和 MCP 工具面的前提下，完成 Assist、Outsource、Review 三种由 GUI 手动选择的协作模式。

### 12.2 交付物

- Assist：Codex主导，最多初答 + 1 次聚焦追问。
- Outsource：结构化上下文包、多轮复用，默认最多 6 个 Copilot turn。
- Review：两个相互隔离的 reviewer conversation 串行执行。
- Reviewer A：复杂度、边界和替代方案。
- Reviewer B：故障模式、证据和可验证性。
- consultation ID 与 conversation URL 的最小映射。
- GUI 最小咨询记录，不默认保存正文。

### 12.3 模式硬规则

- 模式只能在 GUI 手动切换。
- MCP schema 中仍然没有 `mode`。
- v1 不自动从 Assist 升级到 Outsource/Review。
- Review 不并发写入页面，不使用多数票替代 Codex裁决。
- 所有模式继续使用 Opus → GPT 5.6 Think deeper → 深度思考优先级。

### 12.4 验收

- 三种模式均可以从 GUI 选择，并只影响下一次咨询。
- Assist 追问复用原 conversation。
- Outsource 达到回合预算后停止，不无限讨论。
- Review 使用两个隔离 conversation，角色提示互不泄漏。
- Codex能够列出两位 reviewer 的一致点、分歧和最终裁决。
- 不存储完整 prompt/reply 正文。
- 完整 v1 生产代码仍以 7000 行为上限目标。
- 本地提交：`phase 5: complete manual collaboration modes`。

### 12.5 停止条件

如果实现三模式需要新增 MCP 工具、任务队列、多标签并发或通用 Agent 框架，应停止并回到单标签、串行会话设计。

### 12.6 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 5：完整三模式。
在不修改两个 MCP 工具和单标签串行架构的前提下，实现 Assist、Outsource、
Review。模式只能由 GUI 手动选择，不实现自动路由；Review 使用两个隔离
conversation 串行执行，最终由 Codex按证据裁决。
完成全部模式验收、保持生产代码预算、更新路线图并创建阶段提交后停止。
```

## 13. Phase 6：团队分发

### 13.1 目标

把本机可用版本整理为团队成员可以安装、绑定自己的 Edge/Copilot 账号并在 Codex 中使用的内部 v1。

### 13.2 交付物

- `win-x64` self-contained 发布包。
- 稳定安装目录，例如 `%LOCALAPPDATA%\Programs\CopilotBridge`。
- 简单、幂等的内部安装/卸载脚本和开始菜单入口。
- `.codex-plugin/plugin.json`。
- `.mcp.json`。
- `copilot-consult` Skill 和必要 assets。
- `INSTALL.md`、`TEAM-ROLLOUT.md`、`TROUBLESHOOTING.md`。
- 本机全新安装演练。
- 本机隔离分发验收与真实日常 Edge 后台咨询。

首个团队版不建设公共市场、在线更新服务、遥测后台或复杂安装器。如果稳定路径的内部安装包足够，不引入 MSI/MSIX 工程。

### 13.3 G7–G8

- **G7**：Edge 重启、远程调试关闭、登录失效、标签页关闭和模型回退均产生预期结果。
- **G8**：临时隔离的 `%LOCALAPPDATA%`、`CODEX_HOME` 与开始菜单环境能够完成应用/Plugin 安装、MCP 启动和卸载，宿主配置保持不变；用户完成 Edge 远程访问授权后，本机真实日常 Edge 能由 Codex 完成一次后台 Assist，且例行咨询不抢占前台窗口。

### 13.4 明确不做

- 不推送公开市场。
- 不自动上传遥测。
- 不自动配置企业管理员策略。
- 不托管团队账号或 cookie。
- 不添加自动更新服务器。
- 不发布到未经用户授权的 GitHub 或其他远程仓库。

### 13.5 验收

- 本机卸载后可按文档重新安装并工作。
- 安装不会覆盖用户 Edge 配置档或 Codex 其他 MCP 配置。
- 真实 Edge 使用当前用户自己的现有 Microsoft 365 登录。
- G7–G8 全部通过。
- 本地提交：`phase 6: package team v1`。

### 13.6 外部兼容性边界

用户已决定将原第二台电脑 G8 改为本机隔离验收。本机隔离环境必须覆盖：

- 临时且独立的 `%LOCALAPPDATA%`、`CODEX_HOME` 和开始菜单目录；
- 应用、快捷方式、marketplace 与 Plugin 的安装和卸载；
- 安装后 MCP 进程能够启动；
- 卸载后隔离环境清理完成，宿主 Codex 配置和用户数据不变；
- 真实日常 Edge 中完成一次后台 Assist；浏览器远程访问授权提示单独记录，授权后的例行咨询监测前台不被 Edge 或 Bridge 抢占。

不同硬件、Windows build、Microsoft 365 账号、tenant 和企业策略仍可能暴露兼容性差异，应在 v1 后团队推广时继续试点；它们不是修订后 G8 的通过条件，也不得被描述为已经验证。

### 13.7 可复制 Goal

```text
严格按照 EXECUTION-ROADMAP.md 完成 Phase 6：团队分发。
制作最小内部 Windows 发布包和 Codex Plugin，完成安装、团队部署和故障排查
文档，并执行本机全新安装与 G7。不得建设公共市场、自动更新、遥测后台或
复杂安装器。
在本机临时隔离的 LOCALAPPDATA、CODEX_HOME 和开始菜单环境完成 G8 的安装、
MCP 启动、卸载和宿主配置保护，再以真实日常 Edge 完成后台 Assist 与前台
无抢占检查。不同硬件、账号和企业策略环境留作 v1 后团队试点。
```

## 14. 阶段状态记录

执行过程中只更新下表，不另建复杂证据系统。

| 阶段 | 状态 | 开始时间 | 完成时间 | 实耗 | Git 提交 | 验证摘要/阻塞 |
|---|---|---|---|---:|---|---|
| Phase 0 | 通过 | 2026-07-18 02:57 +08:00 | 2026-07-18 02:59 +08:00 | 2 分钟 | `phase-0` | Release 构建 0 警告/0 错误；空 xUnit 项目正常运行；结构为 1 个生产项目 + 1 个测试项目 |
| Phase 1 | 通过 | 2026-07-18 03:06 +08:00 | 2026-07-18 03:26 +08:00 | 20 分钟 | `phase-1` | G1：连接日常 Edge 的完整 WebSocket 端点，唯一 Copilot 页和 background target 验证通过；G2：完整水合等待后选择并读回 Opus；G3：单击发送一次，GPT 5.6 Think deeper 回复 `COPILOT_BRIDGE_TEST_OK`，会话内 prompt/reply 均恰好 1 条。游戏窗口在发送全程保持前台，用户光标可继续移动；Release 0 警告/0 错误，677 LOC、1 个直接依赖、0 个输入模拟 API |
| Phase 2 | 通过 | 2026-07-18 03:27 +08:00 | 2026-07-18 03:36 +08:00 | 9 分钟 | `phase-2` | Settings Store、单一 M365 selector 资源、Edge Session Adapter、Page Driver、Markdown 提取与 Assist Coordinator 已完成；13/13 fixture 测试覆盖延迟菜单、Opus/GPT/深度思考回退、GPT 5.6/5.5 区分、零发送、生成完成/超时/页面错误、发送边界、Markdown 与显式 conversation URL 追问。真实 Coordinator 以 Opus 单击发送并收到 `COPILOT_BRIDGE_PHASE2_OK`；最终实页回归通过。Release 0 警告/0 错误，1099 LOC、1 个直接依赖 |
| Phase 3 | 通过 | 2026-07-18 03:38 +08:00 | 2026-07-18 04:05 +08:00 | 27 分钟 | `phase-3` | 单一 WPF EXE 完成概览、协作、浏览器与模型三页；首次 Edge 授权后窗口级 CDP 会话持续复用，绑定、两页设置保存和 Opus 测试咨询均未再次弹出授权。实页收到唯一回复 `COPILOT_BRIDGE_PHASE3_OK`；配置仅持久化模式、超时与 conversation URL，未保存 prompt/reply。Design QA 通过；Release 0 警告/0 错误，13/13 测试通过，1944 LOC、1 个直接依赖、0 个输入模拟 API |
| Phase 4 | 通过 | 2026-07-18 04:06 +08:00 | 2026-07-18 05:10 +08:00 | 64 分钟 | `phase-4` | 同一 EXE 的官方 SDK STDIO MCP 只暴露两个诚实注解工具；repo Skill、项目级逐工具预授权、协议与退出测试均通过。G4：Codex MCP 收到 `COPILOT_BRIDGE_G4_OK` 并继续原任务。G5：同一 consultation ID 与 conversation URL 完成 01–10 十个唯一请求；所有请求均单击一次、零重发，03–10 返回精确 token；长会话 DOM 虚拟化误判已用专门 fixture 修复。G6：咨询期间前台计算器、焦点和值不变。跨进程锁下真实 MCP 后发写入立即返回 `blocked/busy`、零回复、零发送。Release 25/25 测试、Skill 校验、0 警告/0 错误；2535 LOC、2 个直接依赖。企业审批由真实 destructive/open-world 注解与 Codex 策略执行，Bridge 不绕过。 |
| Phase 5 | 通过 | 2026-07-18 05:10 +08:00 | 2026-07-18 05:42 +08:00 | 32 分钟 | `phase-5` | GUI 真实保存并读回 Assist、Outsource、Review，MCP schema 仍无 `mode`。Assist 复用由 Phase 4 同 conversation 连续调用和 fixture 覆盖；Outsource 实页单次返回 `COPILOT_BRIDGE_OUTSOURCE_OK`，预算硬限 6 turn；Review2 由两个串行、隔离的 Opus conversation 返回 `complexity`/`evidence`，URL 不同，Codex列出一致点、分歧与基于证据的裁决。局部 Review 失败严格零重发，并据此修复新页水合等待与嵌套消息去重。状态仅持久化 ID、模式、回合、URL、时间、状态和模型，GUI 重启显示元数据但正文为“未保存”。Release 34/34 测试、Skill 校验、0 警告/0 错误；2746 LOC、2 个直接依赖。默认模式已由 GUI 恢复 Assist。 |
| Phase 6 | 通过 | 2026-07-18 05:42 +08:00 | 2026-07-19 10:27 +08:00 | 32 分钟初始 RC + RC2–RC5 加固 | `phase-6-rc.5` | `win-x64` self-contained 包、稳定安装目录、幂等安装/卸载、开始菜单、团队 marketplace、Plugin、MCP、Skill 与部署文档均完成。G7 的 Edge 重启、远程调试关闭、标签页关闭、登录失效和模型回退均返回预期结果。RC2 将“最近咨询”移至一级“历史对话”；RC3 完成升级事务式回退；RC4 改用 Codex CLI 官方 JSON 查询并补齐卸载事务边界。用户于 2026-07-19 将 G8 修订为本机隔离门禁：临时独立的 `LOCALAPPDATA`、`CODEX_HOME` 和开始菜单环境完成 RC5 安装、Plugin 启用、MCP 启动、卸载与清理，宿主配置哈希保持不变。实页验收发现并修复 `newConversation=true` 仍复用绑定 URL 的问题；RC5 两次新 Assist 均由 Opus 完成，conversation URL 分别为 `.../9e4946e0-124d-47ba-8bf9-b76fb95a1d83` 与 `.../375adfcd-f168-4e05-9dea-7d8ca1256a23`。Edge 首次远程访问授权提示会进入前台，这是不可绕过的浏览器安全边界；同一已授权会话的后续例行 Assist 监测为 Edge/Bridge 前台切换 0 次。Release 49/49 测试通过。不同硬件、账号、tenant 和企业策略环境保留为 v1 后团队试点，不描述为已验证。RC5 ZIP SHA-256：`b31bb22178356fd94b707db77f29f8f00bee5aa450d3c751e4f791e6b9714a23`。 |
| Phase 7 | 通过 | 2026-07-19 11:16 +08:00 | 2026-07-19 11:37 +08:00 | 21 分钟 | `phase-7` | v1.1 会话工作台完成第一条真实纵切：即时咨询把实际发送/接收 Markdown、角色、已验证模型和时间原子写入用户本地工作区；历史页可创建项目、创建会话、编辑本地标题并保留 Copilot 标题字段、通过下拉或拖拽移动 Markdown 文件、检索会话正文及复制 Markdown。旧网页历史不自动导入，MCP 工具面未扩大。Debug UI 启动检查通过；Debug 50/50 测试、0 警告/0 错误。常规 Release 输出被用户已有的 3 个 CopilotBridge 进程锁定，使用隔离输出完成 Release 编译验证。 |
| Phase 8 | 通过 | 2026-07-19 12:00 +08:00 | 2026-07-19 12:16 +08:00 | 16 分钟 | `phase-8` | 概览状态采用自适应 CDP/DOM 读取：前台概览 10 秒、后台/其他页面 60 秒；失败按 30/60/120 秒退避；咨询或已有刷新运行时跳过，不并发刷新。UI 显示上次检查和下次预计检查，手动刷新保留。测试覆盖 8 个间隔组合；Debug 58/58 测试、0 警告/0 错误。 |
| Phase 9 | 通过 | 2026-07-19 12:16 +08:00 | 2026-07-19 12:19 +08:00 | 3 分钟 | `phase-9` | 主导航新增一级“设置”，语言选项支持中文和 English；选择后立即更新 Bridge 界面，并写入现有原子 `settings.json`。用户项目名、Markdown 会话、Copilot 标题与正文保持原样。新增设置持久化与文案选择测试；Debug 59/59 测试、0 警告/0 错误。 |
| Phase 10 | 通过 | 2026-07-19 15:01 +08:00 | 2026-07-19 15:12 +08:00 | 11 分钟 | `phase-10` | 历史页新增“导入当前旧对话”。仅读取当前唯一 Copilot 对话的 DOM 已加载消息，预览标题、URL、用户/Copilot 消息数与本地留存提示，用户确认后写入一个 Markdown；不滚动、导航、发送或批量扫描。实页成功导入一份旧对话：URL、Copilot 标题、消息顺序均已留存，历史 Copilot 回复均为 `unknown` 模型状态；URL 重复导入由专用测试拦截。Debug 60/60 测试、0 警告/0 错误。 |

允许状态只有：`未开始`、`进行中`、`通过`、`阻塞`、`部分完成`。

## 15. 项目完成定义

只有以下条件全部成立，整个目标才完成：

- Phase 0–6 均为“通过”。
- G1–G8 均有真实验证结果。
- 日常 Edge 后台咨询无前台抢占。
- 连续 10 次咨询无重复发送。
- 三种模式由 GUI 手动选择，MCP 无权切换。
- 只有一个生产项目、一个测试项目和一个生产可执行文件。
- 没有恢复 Frozen 项目或引入其运行时依赖。
- G8 本机隔离环境完成安装、MCP 启动、卸载和宿主配置保护，真实日常 Edge 完成后台 Assist。
- 文档与最终实现一致。

“代码已写完”“本机运行过”或“发布包已生成”都不足以单独完成总 Goal。

## 16. Phase 7：v1.1 会话工作台纵切

### 16.1 目标

在不改变 v1 后台咨询边界的前提下，让 Bridge 管理的新即时咨询成为完整、可本地分类和检索的 Markdown 会话。此阶段先完成真实纵切，不导入旧网页历史，也不扩大 MCP 工具面。

### 16.2 交付物

- 用户可见的本地工作区、收件箱、独立对话和项目文件夹。
- 一会话一 Markdown 文件，记录即时发送和接收正文、角色、时间、实际模型和验证状态。
- 稳定会话 ID、Copilot URL、Copilot 初始/当前标题/标题历史与本地显示标题。
- 历史对话三栏工作台：项目、会话列表、详情；支持新项目、移动、改名、会话内检索和复制 Markdown。
- 现有 GUI 测试咨询成功后自动写入即时会话；不改变现有网页发送行为。

### 16.3 明确不做

- 不自动读取或批量导入旧 Copilot 网页对话。
- 不增加 MCP 工具、HTTP 服务、数据库、队列或第二个浏览器栈。
- 不向 Agent 自动暴露整个工作区或整个项目内容。
- 不实现跨项目复制会话正文。

### 16.4 验收

- 通过单元测试验证 Markdown 原子写入、双标题、本地重命名、项目移动和发送/回复关键词检索。
- 非开发人员可以创建项目、完成一次即时咨询、查看完整 Markdown、改名、移动和检索。
- `dotnet build CopilotBridge.sln` 与 `dotnet test CopilotBridge.sln --no-build` 通过。
- 不执行额外真实 Copilot 发送；已有实页发送语义与 G1–G8 记录不被改写。
- 本地提交：`phase 7: deliver v1.1 conversation workspace`。
