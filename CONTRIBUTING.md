# Contributing to Copilot Bridge

Thank you for improving Copilot Bridge. Please discuss substantial changes in
an Issue or Discussion before opening a pull request.

## Contribution boundaries

- Do not submit credentials, cookies, access tokens, real Copilot
  conversation URLs, personal data, tenant data, screenshots of private
  environments, or local machine configuration.
- Keep the production boundary intact: one production executable, STDIO MCP,
  Edge CDP/DOM for the bound Copilot tab, and no foreground-input fallback.
- Do not add a database, local web server, daemon, queue, generic provider
  framework, or a second browser-automation stack without prior agreement.

## Pull requests

1. Create a focused branch from `main`.
2. Keep the change scoped and update documentation or tests when behavior
   changes.
3. Run the required checks from the repository root:

   ```powershell
   dotnet build CopilotBridge.sln
   dotnet test CopilotBridge.sln --no-build
   ```

4. Explain the user-visible behavior, verification evidence, and any known
   limitations in the pull request.

## Contribution licensing

By submitting a contribution, you represent that you have the right to submit
it and agree that your contribution may be distributed under the Apache
License, Version 2.0.
