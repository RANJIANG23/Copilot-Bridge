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

function Get-ConfiguredMarketplaceRoot {
    param(
        [string[]]$Lines,
        [string]$Name
    )

    $inlineLine = $Lines |
        Where-Object { $_ -match "^$([regex]::Escape($Name))\s+" } |
        Select-Object -First 1
    if ($inlineLine) {
        return ($inlineLine -replace "^$([regex]::Escape($Name))\s+", '').Trim()
    }

    $header = 'Marketplace `' + $Name + '`'
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index].Trim() -ne $header) {
            continue
        }

        for ($candidate = $index + 1; $candidate -lt $Lines.Count; $candidate++) {
            $value = $Lines[$candidate].Trim()
            if ($value) {
                return $value
            }
        }
    }

    return $null
}

if (-not $SkipCodexPlugin -and (Get-Command codex -ErrorAction SilentlyContinue)) {
    $marketplaceLines = @(& codex plugin marketplace list 2>&1)
    if ($LASTEXITCODE -eq 0) {
        $existingRoot = Get-ConfiguredMarketplaceRoot $marketplaceLines $marketplaceName
        if ($existingRoot) {
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
