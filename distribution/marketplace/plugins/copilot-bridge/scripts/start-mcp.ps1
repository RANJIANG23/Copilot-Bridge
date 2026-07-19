$ErrorActionPreference = 'Stop'

$executable = Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge\CopilotBridge.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    [Console]::Error.WriteLine(
        "Copilot Bridge is not installed at the expected path: $executable")
    exit 1
}

$arguments = @('--mcp')
if (-not [string]::IsNullOrWhiteSpace($env:COPILOT_BRIDGE_SETTINGS_PATH)) {
    $arguments += @('--settings-path', $env:COPILOT_BRIDGE_SETTINGS_PATH)
}

& $executable @arguments
exit $LASTEXITCODE
