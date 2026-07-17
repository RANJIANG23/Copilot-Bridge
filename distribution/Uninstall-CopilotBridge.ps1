[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'),
    [switch]$RemoveUserData,
    [switch]$SkipCodexPlugin
)

$ErrorActionPreference = 'Stop'
$marketplaceName = 'copilot-bridge-team'
$pluginSelector = "copilot-bridge@$marketplaceName"
$installedMarketplace = Join-Path $InstallDirectory 'marketplace'

if (-not $SkipCodexPlugin -and (Get-Command codex -ErrorAction SilentlyContinue)) {
    $marketplaceLines = @(& codex plugin marketplace list 2>&1)
    if ($LASTEXITCODE -eq 0) {
        $existingLine = $marketplaceLines |
            Where-Object { $_ -match "^$([regex]::Escape($marketplaceName))\s+" } |
            Select-Object -First 1
        if ($existingLine) {
            $existingRoot = ($existingLine -replace "^$([regex]::Escape($marketplaceName))\s+", '').Trim()
            if ([IO.Path]::GetFullPath($existingRoot) -eq [IO.Path]::GetFullPath($installedMarketplace)) {
                $pluginLines = @(& codex plugin list 2>&1)
                if ($LASTEXITCODE -eq 0 -and
                    ($pluginLines | Where-Object { $_ -match "^$([regex]::Escape($pluginSelector))\s+installed" })) {
                    & codex plugin remove $pluginSelector --json
                    if ($LASTEXITCODE -ne 0) {
                        throw 'Unable to remove the Copilot Bridge plugin.'
                    }
                }
                & codex plugin marketplace remove $marketplaceName --json
                if ($LASTEXITCODE -ne 0) {
                    throw 'Unable to remove the Copilot Bridge marketplace.'
                }
            }
            else {
                Write-Warning "Marketplace '$marketplaceName' points elsewhere and was not changed: $existingRoot"
            }
        }
    }
}

$installedExecutable = Join-Path $InstallDirectory 'CopilotBridge.exe'
Get-Process -Name CopilotBridge -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExecutable } |
    Stop-Process -Force

$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Copilot Bridge.lnk'
if (Test-Path -LiteralPath $startMenu) {
    Remove-Item -LiteralPath $startMenu -Force
}
if (Test-Path -LiteralPath $InstallDirectory) {
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
}
if ($RemoveUserData) {
    $userData = Join-Path $env:LOCALAPPDATA 'CopilotBridge'
    if (Test-Path -LiteralPath $userData) {
        Remove-Item -LiteralPath $userData -Recurse -Force
    }
}

Write-Host 'Copilot Bridge uninstalled.'
if (-not $RemoveUserData) {
    Write-Host 'User settings and consultation metadata were preserved.'
}
