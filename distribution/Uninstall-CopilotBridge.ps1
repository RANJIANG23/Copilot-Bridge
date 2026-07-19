[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'),
    [string]$StartMenuDirectory = [Environment]::GetFolderPath('Programs'),
    [switch]$RemoveUserData,
    [switch]$SkipCodexPlugin
)

$ErrorActionPreference = 'Stop'
$marketplaceName = 'copilot-bridge-team'
$pluginSelector = "copilot-bridge@$marketplaceName"
$installedMarketplace = Join-Path $InstallDirectory 'marketplace'

if (-not $SkipCodexPlugin -and (Get-Command codex -ErrorAction SilentlyContinue)) {
    $marketplaceOutput = @(& codex plugin marketplace list --json 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read configured Codex plugin marketplaces. Nothing was removed.'
    }
    try {
        $marketplaces = (($marketplaceOutput -join "`n") | ConvertFrom-Json).marketplaces
    }
    catch {
        throw 'Codex returned an invalid marketplace list. Nothing was removed.'
    }

    $existingRoot = @($marketplaces) |
        Where-Object name -eq $marketplaceName |
        Select-Object -ExpandProperty root -First 1
    if ($existingRoot) {
        if ([IO.Path]::GetFullPath($existingRoot) -eq [IO.Path]::GetFullPath($installedMarketplace)) {
            $pluginOutput = @(& codex plugin list --json 2>&1)
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to read installed Codex plugins. Nothing was removed.'
            }
            try {
                $plugins = (($pluginOutput -join "`n") | ConvertFrom-Json).installed
            }
            catch {
                throw 'Codex returned an invalid plugin list. Nothing was removed.'
            }

            $pluginWasRemoved = $false
            if (@($plugins) | Where-Object { $_.pluginId -eq $pluginSelector -and $_.installed }) {
                & codex plugin remove $pluginSelector --json
                if ($LASTEXITCODE -ne 0) {
                    throw 'Unable to remove the Copilot Bridge plugin. Nothing else was removed.'
                }

                $pluginWasRemoved = $true
            }

            try {
                & codex plugin marketplace remove $marketplaceName --json
                if ($LASTEXITCODE -ne 0) {
                    throw 'Unable to remove the Copilot Bridge marketplace.'
                }
            }
            catch {
                $marketplaceError = $_
                if ($pluginWasRemoved) {
                    & codex plugin add $pluginSelector --json
                    if ($LASTEXITCODE -ne 0) {
                        throw "$($marketplaceError.Exception.Message) Plugin rollback also failed."
                    }
                }

                throw $marketplaceError
            }
        }
        else {
            Write-Warning "Marketplace '$marketplaceName' points elsewhere and was not changed: $existingRoot"
        }
    }
}

$installedExecutable = Join-Path $InstallDirectory 'CopilotBridge.exe'
Get-Process -Name CopilotBridge -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExecutable } |
    Stop-Process -Force

$startMenu = Join-Path $StartMenuDirectory 'Copilot Bridge.lnk'
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
