[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'),
    [switch]$SkipCodexPlugin
)

$ErrorActionPreference = 'Stop'
$marketplaceName = 'copilot-bridge-team'
$pluginSelector = "copilot-bridge@$marketplaceName"
$appSource = Join-Path $PSScriptRoot 'app'
$marketplaceSource = Join-Path $PSScriptRoot 'marketplace'
$defaultInstallDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'

if (-not $SkipCodexPlugin -and
    [IO.Path]::GetFullPath($InstallDirectory) -ne [IO.Path]::GetFullPath($defaultInstallDirectory)) {
    throw 'A custom install directory is supported only with -SkipCodexPlugin.'
}

if (-not (Test-Path -LiteralPath (Join-Path $appSource 'CopilotBridge.exe') -PathType Leaf)) {
    throw 'The release package is incomplete: app\CopilotBridge.exe was not found.'
}
if (-not (Test-Path -LiteralPath (Join-Path $marketplaceSource '.agents\plugins\marketplace.json') -PathType Leaf)) {
    throw 'The release package is incomplete: marketplace manifest was not found.'
}
if (-not $SkipCodexPlugin -and -not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw 'Codex CLI was not found. Install Codex or rerun with -SkipCodexPlugin for app-only installation.'
}

$installParent = Split-Path $InstallDirectory -Parent
$stage = Join-Path $installParent ('.CopilotBridge.install-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $installParent ('.CopilotBridge.backup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $appSource -Force |
        Copy-Item -Destination $stage -Recurse -Force
    Copy-Item -LiteralPath $marketplaceSource -Destination $stage -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-CopilotBridge.ps1') -Destination $stage -Force
    foreach ($document in 'INSTALL.md', 'TEAM-ROLLOUT.md', 'TROUBLESHOOTING.md', 'SHA256SUMS.txt') {
        $source = Join-Path $PSScriptRoot $document
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $stage -Force
        }
    }

    $installedExecutable = Join-Path $InstallDirectory 'CopilotBridge.exe'
    Get-Process -Name CopilotBridge -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $installedExecutable } |
        Stop-Process -Force

    if (Test-Path -LiteralPath $InstallDirectory) {
        Move-Item -LiteralPath $InstallDirectory -Destination $backup
    }
    Move-Item -LiteralPath $stage -Destination $InstallDirectory

    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Copilot Bridge.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startMenu)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.Description = 'Configure and diagnose Copilot Bridge'
    $shortcut.Save()

    if (-not $SkipCodexPlugin) {
        $installedMarketplace = Join-Path $InstallDirectory 'marketplace'
        # Codex CLI owns its plugin registration. This script never edits config.toml directly.
        $marketplaceLines = @(& codex plugin marketplace list 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to read configured Codex plugin marketplaces.'
        }
        $existingLine = $marketplaceLines |
            Where-Object { $_ -match "^$([regex]::Escape($marketplaceName))\s+" } |
            Select-Object -First 1
        if ($existingLine) {
            $existingRoot = ($existingLine -replace "^$([regex]::Escape($marketplaceName))\s+", '').Trim()
            if ([IO.Path]::GetFullPath($existingRoot) -ne [IO.Path]::GetFullPath($installedMarketplace)) {
                throw "Marketplace '$marketplaceName' already points to another location: $existingRoot"
            }
        }
        else {
            & codex plugin marketplace add $installedMarketplace --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to register the Copilot Bridge marketplace.'
            }
        }

        $pluginLines = @(& codex plugin list 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to read installed Codex plugins.'
        }
        if ($pluginLines | Where-Object { $_ -match "^$([regex]::Escape($pluginSelector))\s+installed" }) {
            & codex plugin remove $pluginSelector --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to remove the previous Copilot Bridge plugin version.'
            }
        }
        & codex plugin add $pluginSelector --json
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to install the Copilot Bridge plugin.'
        }
    }
}
catch {
    if (-not (Test-Path -LiteralPath $InstallDirectory) -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $InstallDirectory
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}

Write-Host "Copilot Bridge installed at: $InstallDirectory"
if (-not $SkipCodexPlugin) {
    Write-Host 'Codex Plugin installed. Start a new Codex task before testing it.'
}
