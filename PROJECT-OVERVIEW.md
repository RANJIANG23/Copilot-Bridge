# Copilot Bridge 项目说明

Copilot Bridge 是一个运行在 Windows 本机的桥接工具，用于以受控方式把 Microsoft 365 Copilot 的答复接入 Codex 工作流，作为 Codex 的第二模型意见来源。

## 项目定位

Copilot Bridge 不是独立智能体，也不是 Copilot 的替代品或增强层。它是一个本地通道：将 Codex 通过 STDIO MCP 发出的咨询请求交给用户已登录 Microsoft Edge 中的专用后台标签页，并把 Microsoft 365 Copilot 的回复返回给 Codex。

- Copilot 只提供可供参考的第二意见，不承担执行或授权职责。
- 核验、裁决与实际执行始终由 Codex 负责。
- 本项目是独立工具，不是 Microsoft、OpenAI 或 Anthropic 的官方产品，也不代表或获得其背书。

## 工作方式

```text
Codex → STDIO MCP → CopilotBridge.exe → Edge CDP/DOM → 专用后台 Edge 标签页 → Microsoft 365 Copilot → 原路返回
```

Bridge 只发布一个生产可执行文件 `CopilotBridge.exe`。GUI 与 MCP 入口共用同一业务代码和本地配置；MCP 可在 GUI 关闭时工作。

- Bridge 通过 Edge CDP 和页面 DOM 与 Copilot 交互；用户的登录状态与会话仍由 Edge 维护。
- 协作模式只能在 GUI 中手动选择：Assist、Outsource、Review。
- 模型优先级由 GUI 配置：Opus、GPT 5.6 Think deeper、深度思考。
- MCP 调用不接受 `mode` 或 `model` 参数，避免上游调用意外覆盖用户选择。

## 核心能力

- **STDIO MCP 接入**：Codex 可通过最小工具面征询 Copilot 并获得结构化结果。
- **绑定式后台会话**：Bridge 仅操作用户明确绑定的 Copilot 标签页，支持会话复用。
- **可见的本地控制**：GUI 提供协作模式、模型优先级、连接状态和诊断入口。
- **一次性发送保护**：发送状态不确定时绝不自动重发，避免重复咨询。
- **本地会话工作台**：用户明确触发的即时咨询可保存为本地 Markdown 会话，支持按项目分类、改名、检索和复制；不会自动导入既有 Copilot 网页历史。

## 明确边界

- **产品形态**：仅一个生产项目、一个测试项目和一个生产可执行文件；不引入数据库、本地 Web 服务、守护进程、消息队列或第二套浏览器自动化。
- **交互方式**：不使用 Computer Use、OCR、Windows UI Automation、物理鼠标键盘模拟、前台窗口切换、网络劫持或流量重写作为运行时方案。
- **数据范围**：不保存 Edge Cookie、令牌或其他标签页正文。仅用户明确触发并由 Bridge 实际发送、接收的即时咨询 Markdown 会写入本地工作区。
- **职责范围**：Copilot 回复不是执行指令或授权依据；它不能扩大用户指定的工作范围。
- **功能范围**：不提供跨模型自动路由、通用网页自动化、集中账号托管、自动更新服务或企业策略绕过。

## 当前状态

- 团队 v1 的 Phase 0–6 与 G1–G8 已通过。
- v1.1 的 Phase 7–9 已通过，实现了会话工作台、自适应状态刷新和中英界面切换；该开发版本尚未作为新的安装包发布。
- 验证范围是本机隔离分发环境和本机真实日常 Edge。不同硬件、Microsoft 365 账号、租户与企业策略环境仍属于后续试点，不能声称已经验证。

## 适用对象

- 希望在 Codex 工作流中获得 Microsoft 365 Copilot 第二意见的开发者和工程师。
- 关注协作边界、数据留存和验证范围的技术决策者。
- 愿意在受控环境中参与后续兼容性试点，并理解当前验证范围的团队成员。

本工具不适用于要求跨设备、跨账号或跨企业策略即插即用的部署，也不适用于将 Copilot 作为自动化执行主体、身份来源或最终决策者的场景。
