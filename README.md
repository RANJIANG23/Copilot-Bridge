# Copilot Bridge

Copilot Bridge lets Codex consult a signed-in Microsoft 365 Copilot through a dedicated background tab in the user's daily Microsoft Edge profile.

The repository is being implemented in strict phases. Read these documents before changing code:

- [Project design](./PROJECT-DESIGN.md)
- [Execution roadmap](./EXECUTION-ROADMAP.md)

## Current status

Phase 0 repository baseline is complete. Phase 1 browser automation has not been implemented yet.

## Build

```powershell
dotnet build CopilotBridge.sln
dotnet test CopilotBridge.sln --no-build
```

The v1 architecture is intentionally limited to one production project, one test project, and one production executable.
