# Phase 13 Design QA

final result: passed

## Audit scope

- Product: Copilot Bridge v1.1.2 Debug build.
- User goal: manage local conversations and settings with a Microsoft Copilot-like light/dark visual system, protected uncategorized conversations, understandable sorting, and working shortcut actions.
- Capture method: current-run Computer Use screenshots at the app's 1080 x 640 viewport, followed by direct inspection and side-by-side comparison with the supplied Microsoft Copilot references.
- Local evidence folder: `artifacts/design-qa/phase13-current/` (ignored from Git because screenshots can contain local conversation metadata).

## Step results

| Step | Description | Health | Evidence and findings |
|---:|---|---|---|
| 1 | Light overview | Good | `01-light-overview.png`; neutral canvas, sidebar, cards, typography, and active navigation are coherent and unclipped. |
| 2 | Light conversation management | Good after fix | `02-light-conversation-metadata-hidden.png`, `03-light-conversation-maximized.png`; the internal Base64 metadata comment was initially visible, then removed from display while remaining in the stored Markdown. |
| 3 | Light settings | Good | `04-light-settings-top.png`; settings hierarchy and action placement align with the supplied Copilot reference. |
| 4 | Dark settings and actions | Good | `05-dark-settings-top.png`, `06-dark-settings-actions.png`; the low-glare neutral hierarchy, shortcut cards, and owning-card action positions are consistent. |
| 5 | Dark conversation management | Good | `07-dark-conversation.png`; the locked system project and draggable custom projects remain legible with no clipping. |
| 6 | Project menu and pin state | Good | `08a-dark-project-context.png`, `09-dark-project-pinned.png`; only custom projects expose pin/rename/delete, and pinning updates the card and order. |
| 7 | Project and model drag sorting | Good after fix | Real isolated-workspace project drag persisted `Beta` before `Alpha`. Real model drag moved `GPT 5.6 Think deeper` before `Opus`, then restored the original order. The first model test exposed selection-on-hover choosing the wrong drag source; mouse-down source capture fixed project, conversation, and model handlers. |
| 8 | Shortcut action | Good | The desktop-shortcut action completed and created an idempotent `Copilot Bridge` desktop shortcut. Taskbar and Start actions retain their honest Windows-assisted fallback instead of bypassing OS confirmation. |

## Strengths

- Light and dark themes use the same spacing, radius, card, focus, and hierarchy rules.
- `未分类对话` is visually and behaviorally distinct as the fixed system project.
- Drag handles make custom-project and model sorting discoverable.
- Save and send actions are consistently attached to the top-right of the card they affect.
- Shortcut actions follow the supplied Copilot information architecture without pretending Windows supports silent pinning.

## Corrected findings

- P1: the conversation detail initially exposed the internal `copilot-bridge-conversation` Base64 comment. The UI now renders a display-only Markdown projection; the persisted file is unchanged. A regression test covers both properties.
- P1: model and conversation drag start could switch from the pressed item to the hovered item before `DoDragDrop`. All three lists now capture the source item on mouse-down. Real model and project reordering passed after the correction.

## Accessibility and evidence limits

- Visible focus outlines, labels, drag-handle descriptions, lock affordance, and text hierarchy were present in captured states.
- Screenshots do not prove full WCAG contrast, screen-reader order, high-contrast mode, reduced-motion behavior, or 200% reflow; these remain release notes rather than compliance claims.
- Computer Use serializes input and capture, so it cannot save a screenshot while the pointer is physically held mid-drag. The persisted final order, immediate post-drop visual state, shared animation code path, and automated persistence tests were used as the repeatable drag evidence.
- No Copilot message was sent during this audit. The test workspace contained synthetic project names only and the user's normal workspace setting was restored afterward.

## Completion gate

Phase 13 passes: required light/dark surfaces were captured and compared, the two interaction defects found by the audit were corrected, real project/model drag paths and shortcut creation completed, and the standard build/test gate passes with 77/77 tests.
