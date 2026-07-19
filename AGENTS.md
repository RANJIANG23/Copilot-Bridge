# Copilot Bridge repository rules

Before working in this repository, read `PROJECT-DESIGN.md` and `EXECUTION-ROADMAP.md` completely.

## Phase discipline

- Use the phase status table in `EXECUTION-ROADMAP.md` as the current implementation boundary.
- Finish and verify the active phase before implementing a later phase.
- Update the roadmap status and create a local phase commit when a phase passes.
- If a phase gate fails, stop at that gate. Do not add an alternate architecture to bypass it.

## Complexity limits

- Keep exactly one production project and one test project through v1.
- Publish only `CopilotBridge.exe` as the production executable.
- Keep production code near 2,500 lines through the first live loop and near 7,000 lines for v1.
- Keep direct production NuGet dependencies at five or fewer.
- Do not add a database, local web server, daemon, queue, custom RPC, generic provider framework, or second browser automation stack.
- Do not create abstractions for hypothetical future providers or migrations.

## Browser boundary

- Routine Copilot interaction must use Edge CDP and DOM operations in the dedicated Copilot tab.
- Never use Computer Use, OCR, UI Automation, physical input simulation, or foreground-window switching as a production fallback.
- Never automatically resend after the submit action becomes uncertain.

## Frozen project boundary

- Do not restore, build, modify, reference, or copy runtime code from the frozen ChatGPT project.
- Reimplement only the small ideas explicitly allowed by `PROJECT-DESIGN.md`.

## Verification

Run from the repository root:

```powershell
dotnet build CopilotBridge.sln
dotnet test CopilotBridge.sln --no-build
```

Add only the live or fixture checks required by the active phase.
