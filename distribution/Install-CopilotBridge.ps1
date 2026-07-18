[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'),
    [string]$StartMenuDirectory = [Environment]::GetFolderPath('Programs'),
    [switch]$SkipCodexPlugin
)

$ErrorActionPreference = 'Stop'
$marketplaceName = 'copilot-bridge-team'
$pluginSelector = "copilot-bridge@$marketplaceName"
$appSource = Join-Path $PSScriptRoot 'app'
$marketplaceSource = Join-Path $PSScriptRoot 'marketplace'
$defaultInstallDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CopilotBridge'

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
$startMenu = Join-Path $StartMenuDirectory 'Copilot Bridge.lnk'
$hadPreviousInstall = Test-Path -LiteralPath $InstallDirectory
$hadPreviousShortcut = Test-Path -LiteralPath $startMenu
$pluginWasInstalled = $false
$pluginWasRemoved = $false
$marketplaceWasAdded = $false
$completed = $false
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

    New-Item -ItemType Directory -Path $StartMenuDirectory -Force | Out-Null
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
        $existingRoot = Get-ConfiguredMarketplaceRoot $marketplaceLines $marketplaceName
        if ($existingRoot) {
            if ([IO.Path]::GetFullPath($existingRoot) -ne [IO.Path]::GetFullPath($installedMarketplace)) {
                throw "Marketplace '$marketplaceName' already points to another location: $existingRoot"
            }
        }
        else {
            & codex plugin marketplace add $installedMarketplace --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to register the Copilot Bridge marketplace.'
            }
            $marketplaceWasAdded = $true
        }

        $pluginLines = @(& codex plugin list 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to read installed Codex plugins.'
        }
        $pluginWasInstalled = [bool]($pluginLines |
            Where-Object { $_ -match "^$([regex]::Escape($pluginSelector))\s+installed" })
        if ($pluginWasInstalled) {
            & codex plugin remove $pluginSelector --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to remove the previous Copilot Bridge plugin version.'
            }
            $pluginWasRemoved = $true
        }
        & codex plugin add $pluginSelector --json
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to install the Copilot Bridge plugin.'
        }
    }

    $completed = $true
}
catch {
    $installError = $_
    $rollbackErrors = [Collections.Generic.List[string]]::new()

    try {
        if (Test-Path -LiteralPath $backup) {
            if (Test-Path -LiteralPath $InstallDirectory) {
                Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
            }
            Move-Item -LiteralPath $backup -Destination $InstallDirectory
        }
        elseif (-not $hadPreviousInstall -and (Test-Path -LiteralPath $InstallDirectory)) {
            Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        }
    }
    catch {
        $rollbackErrors.Add("application rollback failed: $($_.Exception.Message)")
    }

    if (-not $hadPreviousShortcut -and (Test-Path -LiteralPath $startMenu)) {
        try {
            Remove-Item -LiteralPath $startMenu -Force
        }
        catch {
            $rollbackErrors.Add("shortcut rollback failed: $($_.Exception.Message)")
        }
    }

    if (-not $SkipCodexPlugin -and $pluginWasInstalled -and $pluginWasRemoved) {
        try {
            & codex plugin add $pluginSelector --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Codex CLI rejected the previous plugin registration.'
            }
        }
        catch {
            $rollbackErrors.Add("plugin rollback failed: $($_.Exception.Message)")
        }
    }

    if (-not $SkipCodexPlugin -and $marketplaceWasAdded) {
        try {
            & codex plugin marketplace remove $marketplaceName --json
            if ($LASTEXITCODE -ne 0) {
                throw 'Codex CLI rejected marketplace rollback.'
            }
        }
        catch {
            $rollbackErrors.Add("marketplace rollback failed: $($_.Exception.Message)")
        }
    }

    if ($rollbackErrors.Count -gt 0) {
        throw "$($installError.Exception.Message) Rollback was incomplete: $($rollbackErrors -join '; ')"
    }

    throw $installError
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    if ($completed -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}

Write-Host "Copilot Bridge installed at: $InstallDirectory"
if (-not $SkipCodexPlugin) {
    Write-Host 'Codex Plugin installed. Start a new Codex task before testing it.'
}
