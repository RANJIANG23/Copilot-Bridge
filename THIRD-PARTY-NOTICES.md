# Third-party notices

This file records the production NuGet dependency inventory resolved for
Copilot Bridge `1.1.2`. It is included with release archives together
with `LICENSE` and `NOTICE`.

## Direct production dependencies

| Component | Version | License |
|---|---:|---|
| Microsoft.Playwright | 1.61.0 | MIT |
| ModelContextProtocol | 1.4.1 | Apache-2.0 |

## Transitive production dependencies

| Component group | Version | License |
|---|---:|---|
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | MIT |
| Microsoft.Extensions.AI.Abstractions | 10.5.2 | MIT |
| Microsoft.Extensions.* abstractions, options, primitives, and hosting components | 10.0.7 | MIT |
| ModelContextProtocol.Core | 1.4.1 | Apache-2.0 |
| System.ComponentModel.Annotations | 5.0.0 | MIT |

## Bundled runtime components

Self-contained Windows releases include the .NET runtime and Playwright
runtime assets. Playwright's bundled Node and package assets retain their own
`LICENSE`, `NOTICE`, and `ThirdPartyNotices.txt` files under
`app/.playwright/` in each release archive.

## Bundled font

| Component | Source | License |
|---|---|---|
| Noto Sans SC variable font | [Google Fonts `ofl/notosanssc`](https://github.com/google/fonts/tree/main/ofl/notosanssc) | SIL Open Font License 1.1 |

The unmodified font is embedded in `CopilotBridge.exe`. Its complete license is
published as `app/licenses/NotoSansSC-OFL.txt` in every release package.

Before publishing a release, the release owner must review the resolved
production package inventory and the generated archive for any newly bundled
third-party component or notice requirement.
