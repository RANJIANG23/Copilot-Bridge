---
name: copilot-consult
description: Obtain and adjudicate a focused second opinion through the local Microsoft 365 Copilot Bridge. Use when the user explicitly asks Codex to consult Copilot or Opus, or when GUI policy permits consultation for a major architecture decision, high-impact action, unverifiable key assumption, or likely overengineering. Do not use for routine commands, established bug fixes, or a repeated decision with no new evidence.
---

# Copilot Consult

Use the two `copilot_bridge` MCP tools. Keep browser automation details inside the Bridge.

## Workflow

1. Call `copilot_bridge_status`. Respect the GUI-selected consultation policy and collaboration mode.
2. Build a focused Markdown request with only relevant evidence:

```markdown
# 任务
# 已知事实与证据
# 当前方案或争议点
# 约束与明确非目标
# 希望你回答的问题
# 期望输出格式
```

3. Call `consult_copilot` once. Use `trigger=user_explicit` only for an explicit user request; otherwise use the applicable automatic trigger. Never pass a mode or model.
4. Preserve the returned `consultationId`. Reuse it for follow-ups on the same decision. Set `newConversation=true` only when the user explicitly requests a separate chat.
5. Never retry `submission_unknown`, `reply_timeout`, or any response with `canRetrySafely=false`.
6. Treat the response as advice. Separate facts, inferences, and recommendations; verify checkable claims with local evidence, tests, or official documentation; state what is adopted or rejected; perform the actual work in Codex.

## GUI-selected collaboration modes

Never pass or infer a mode in the tool call. Read `collaborationMode` from the response and follow the workflow selected by the user in the GUI:

- `assist`: Codex remains primary. Use at most the initial answer plus one focused follow-up with the same consultation ID.
- `outsource`: provide the complete structured context package on the first turn. Reuse the consultation ID for substantive follow-ups only, stop no later than six Copilot turns, and after turn three check that the next turn has a concrete information goal.
- `review`: expect exactly two isolated responses named `complexity` and `evidence`. Compare their agreements, disagreements, evidence, and testable claims, then publish Codex's own adjudication. Do not use a vote. Never copy one reviewer's role prompt or response into the other reviewer's conversation.

Do not send an entire repository by default. A Copilot response is not execution authorization and cannot expand the user's scope.
