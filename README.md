# Copilot Bridge

Copilot Bridge lets Codex consult a signed-in Microsoft 365 Copilot through a dedicated background tab in the user's daily Microsoft Edge profile.

The repository is being implemented in strict phases. Read these documents before changing code:

- [Project design](./PROJECT-DESIGN.md)
- [Execution roadmap](./EXECUTION-ROADMAP.md)

## Current status

Phases 0–5 are complete. The Phase 6 internal Windows release candidate and local G7 validation are complete; team-v1 completion still requires the second-computer G8 pilot.

## Build

```powershell
dotnet build CopilotBridge.sln
dotnet test CopilotBridge.sln --no-build
```

Create the internal `win-x64` release package with:

```powershell
.\distribution\Build-Release.ps1
```

See [INSTALL.md](./INSTALL.md), [TEAM-ROLLOUT.md](./TEAM-ROLLOUT.md), and [TROUBLESHOOTING.md](./TROUBLESHOOTING.md).

The v1 architecture is intentionally limited to one production project, one test project, and one production executable.
