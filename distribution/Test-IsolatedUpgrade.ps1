[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PreviousZip,
    [Parameter(Mandatory)]
    [string]$CandidateZip,
    [Parameter(Mandatory)]
    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$allowedRoot = Join-Path $repositoryRoot 'artifacts\upgrade-test'
$codexExecutable = Get-ChildItem `
        (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') `
        -Recurse -Filter codex.exe -File |
    Where-Object { $_.DirectoryName -ne (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $codexExecutable) { throw 'Codex CLI executable was not found.' }
$evidencePath = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $evidencePath.StartsWith(
        [IO.Path]::GetFullPath($allowedRoot).TrimEnd('\') + '\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "EvidenceRoot must be a child of $allowedRoot"
}
if (Test-Path -LiteralPath $evidencePath) {
    throw "EvidenceRoot already exists: $evidencePath"
}

$isolatedLocal = Join-Path $evidencePath 'LocalAppData'
$codexTestParent = Join-Path $env:LOCALAPPDATA 'CopilotBridge-Phase23'
$isolatedCodex = Join-Path $codexTestParent ([IO.Path]::GetFileName($evidencePath))
if (Test-Path -LiteralPath $isolatedCodex) {
    throw "Isolated Codex home already exists: $isolatedCodex"
}
$startMenu = Join-Path $evidencePath 'StartMenu'
$previousExtract = Join-Path $evidencePath 'previous-package'
$candidateExtract = Join-Path $evidencePath 'candidate-package'
$isolatedBin = Join-Path $evidencePath 'bin'
New-Item -ItemType Directory -Path `
    $isolatedLocal, $isolatedCodex, $startMenu, $previousExtract, $candidateExtract, $isolatedBin -Force | Out-Null
$codexWrapper = "@echo off`r`n`"$codexExecutable`" %*`r`n"
Set-Content -LiteralPath (Join-Path $isolatedBin 'codex.cmd') `
    -Value $codexWrapper -Encoding ascii
Expand-Archive -LiteralPath $PreviousZip -DestinationPath $previousExtract
Expand-Archive -LiteralPath $CandidateZip -DestinationPath $candidateExtract
$previousPackage = (Get-ChildItem -LiteralPath $previousExtract -Directory | Select-Object -First 1).FullName
$candidatePackage = (Get-ChildItem -LiteralPath $candidateExtract -Directory | Select-Object -First 1).FullName
if (-not $previousPackage -or -not $candidatePackage) {
    throw 'A release ZIP did not contain its expected top-level directory.'
}

$settingsDirectory = Join-Path $isolatedLocal 'CopilotBridge'
$workspace = Join-Path $settingsDirectory 'workspace'
$legacyProject = Join-Path $workspace 'Legacy Project'
$settingsPath = Join-Path $settingsDirectory 'settings.json'
New-Item -ItemType Directory -Path $legacyProject -Force | Out-Null
$deepThinking = -join @([char]0x6df1, [char]0x5ea6, [char]0x601d, [char]0x8003)
[ordered]@{
    edgeUserDataDirectory = (Join-Path $evidencePath 'missing-edge')
    menuMinimumWaitMilliseconds = 2000
    menuMaximumWaitMilliseconds = 6000
    replyTimeoutSeconds = 300
    modelPriority = 'Opus|GPT 5.6 Think deeper|' + $deepThinking
    consultationPolicy = 'ManualOnly'
    collaborationMode = 'Assist'
    displayLanguage = 'Chinese'
    theme = 'Light'
    keepMcpRunningInBackground = $true
    boundConversationUrl = $null
    conversationWorkspaceDirectory = $workspace
    storeConversationContent = $true
} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
Set-Content -LiteralPath (Join-Path $legacyProject '.bridge-project.md') `
    -Value '# Legacy Project' -Encoding utf8
$legacyDocument = [ordered]@{
    id = 'legacy-conversation'
    projectId = 'Legacy Project'
    copilotConversationId = 'legacy'
    copilotConversationUrl = 'https://m365.cloud.microsoft/chat/legacy'
    copilotTitleInitial = 'Legacy Title'
    copilotTitleCurrent = 'Legacy Title'
    copilotTitleHistory = @()
    localTitle = 'Legacy Local Title'
    titleSource = 'local_override'
    mode = 'assist'
    createdAt = '2026-07-19T00:00:00+08:00'
    updatedAt = '2026-07-19T00:00:00+08:00'
    turns = @([ordered]@{
        timestamp = '2026-07-19T00:00:00+08:00'
        role = 'user'
        markdown = 'legacy secret'
        model = $null
        modelStatus = 'not_applicable'
        reviewer = $null
    })
}
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(
        ($legacyDocument | ConvertTo-Json -Depth 8 -Compress)))
Set-Content -LiteralPath (Join-Path $legacyProject 'conversation-legacy-conversation.md') `
    -Value "<!-- copilot-bridge-conversation:$encoded -->`r`n`r`n# Legacy Local Title`r`n" `
    -Encoding utf8

function Get-TreeHash([string]$Path) {
    $prefixLength = [IO.Path]::GetFullPath($Path).TrimEnd('\').Length + 1
    $lines = Get-ChildItem -LiteralPath $Path -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($prefixLength).Replace('\', '/')
            "$relative $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
}

function Install-Package([string]$Package) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $Package 'Install-CopilotBridge.ps1') `
        -StartMenuDirectory $startMenu | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Installer failed: $Package" }
}

function Uninstall-Package([string]$InstallDirectory) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $InstallDirectory 'Uninstall-CopilotBridge.ps1') `
        -StartMenuDirectory $startMenu | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Uninstaller failed: $InstallDirectory" }
}

function Invoke-PackagedMcp([string]$InstallDirectory) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $InstallDirectory 'CopilotBridge.exe'
    $info.Arguments = "--mcp --settings-path `"$settingsPath`""
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $info.Environment['LOCALAPPDATA'] = $isolatedLocal
    $info.Environment['CODEX_HOME'] = $isolatedCodex
    $info.Environment['COPILOT_BRIDGE_SETTINGS_PATH'] = $settingsPath
    $process = [Diagnostics.Process]::Start($info)
    $errorTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"phase19","version":"1"}}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_conversations","arguments":{}}}')
    $process.StandardInput.Flush()
    $outputLines = [Collections.Generic.List[string]]::new()
    $received = [Collections.Generic.HashSet[int]]::new()
    while ($received.Count -lt 3 -and $outputLines.Count -lt 8) {
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait(5000)) {
            $process.Kill()
            throw 'Packaged MCP did not return the expected protocol response.'
        }
        $line = $readTask.Result
        if ($null -eq $line) { break }
        $outputLines.Add($line)
        $message = $line | ConvertFrom-Json
        if ($null -ne $message.id) { $received.Add([int]$message.id) | Out-Null }
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(15000)) {
        $process.Kill($true)
        throw 'Packaged MCP did not exit after EOF.'
    }
    $output = $outputLines -join "`r`n"
    $errorText = $errorTask.GetAwaiter().GetResult()
    Set-Content -LiteralPath (Join-Path $evidencePath 'mcp-stdout.jsonl') `
        -Value $output -Encoding utf8
    Set-Content -LiteralPath (Join-Path $evidencePath 'mcp-stderr.txt') `
        -Value $errorText -Encoding utf8
    if ($process.ExitCode -ne 0) { throw "Packaged MCP failed: $errorText" }
    $messages = @($output -split "`r?`n" |
        Where-Object { $_.Trim() } |
        ForEach-Object { $_ | ConvertFrom-Json })
    $toolNames = @(($messages | Where-Object id -eq 2).result.tools |
        ForEach-Object name | Sort-Object)
    $searchResults = @(($messages | Where-Object id -eq 3).result.structuredContent.results)
    [pscustomobject]@{ ToolNames = $toolNames; SearchResultCount = $searchResults.Count }
}

$hostConfig = Join-Path $env:USERPROFILE '.codex\config.toml'
$hostConfigBefore = if (Test-Path -LiteralPath $hostConfig) {
    (Get-FileHash -LiteralPath $hostConfig -Algorithm SHA256).Hash
} else { 'missing' }
$userDataBefore = Get-TreeHash $settingsDirectory
$originalLocalAppData = $env:LOCALAPPDATA
$originalCodexHome = $env:CODEX_HOME
$originalPath = $env:PATH
try {
    $env:LOCALAPPDATA = $isolatedLocal
    $env:CODEX_HOME = $isolatedCodex
    $env:PATH = $isolatedBin + [IO.Path]::PathSeparator + $originalPath
    Install-Package $previousPackage
    $installDirectory = Join-Path $isolatedLocal 'Programs\CopilotBridge'
    $previousVersion = (Get-Item -LiteralPath `
        (Join-Path $installDirectory 'CopilotBridge.exe')).VersionInfo.ProductVersion

    Install-Package $candidatePackage
    $candidateVersion = (Get-Item -LiteralPath `
        (Join-Path $installDirectory 'CopilotBridge.exe')).VersionInfo.ProductVersion
    $plugin = ((codex plugin list --json | ConvertFrom-Json).installed |
        Where-Object pluginId -eq 'copilot-bridge@copilot-bridge-team' |
        Select-Object -First 1)
    $mcp = Invoke-PackagedMcp $installDirectory
    $userDataAfterMcp = Get-TreeHash $settingsDirectory

    Uninstall-Package $installDirectory
    $uninstallPreservedData = Test-Path -LiteralPath $settingsPath
    $pluginCountAfterUninstall = @((codex plugin list --json | ConvertFrom-Json).installed |
        Where-Object pluginId -eq 'copilot-bridge@copilot-bridge-team').Count
    $marketplaceCountAfterUninstall = @((codex plugin marketplace list --json |
            ConvertFrom-Json).marketplaces |
        Where-Object name -eq 'copilot-bridge-team').Count

    Install-Package $previousPackage
    $rollbackVersion = (Get-Item -LiteralPath `
        (Join-Path $installDirectory 'CopilotBridge.exe')).VersionInfo.ProductVersion
    Uninstall-Package $installDirectory
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $env:CODEX_HOME = $originalCodexHome
    $env:PATH = $originalPath
}
$hostConfigAfter = if (Test-Path -LiteralPath $hostConfig) {
    (Get-FileHash -LiteralPath $hostConfig -Algorithm SHA256).Hash
} else { 'missing' }

$passed = $previousVersion -like '1.2.0*' -and
    $candidateVersion -like '1.2.1*' -and
    $rollbackVersion -like '1.2.0*' -and
    $mcp.ToolNames.Count -eq 4 -and
    $mcp.SearchResultCount -eq 0 -and
    $userDataBefore -eq $userDataAfterMcp -and
    $uninstallPreservedData -and
    $pluginCountAfterUninstall -eq 0 -and
    $marketplaceCountAfterUninstall -eq 0 -and
    $hostConfigBefore -eq $hostConfigAfter
$result = [ordered]@{
    result = if ($passed) { 'passed' } else { 'failed' }
    evidenceRoot = $evidencePath
    previousVersion = $previousVersion
    candidateVersion = $candidateVersion
    rollbackVersion = $rollbackVersion
    pluginVersion = $plugin.version
    mcpTools = $mcp.ToolNames
    legacyOffSearchResults = $mcp.SearchResultCount
    userDataPreserved = ($userDataBefore -eq $userDataAfterMcp)
    uninstallPreservedUserData = $uninstallPreservedData
    pluginRemoved = ($pluginCountAfterUninstall -eq 0)
    marketplaceRemoved = ($marketplaceCountAfterUninstall -eq 0)
    hostConfigPreserved = ($hostConfigBefore -eq $hostConfigAfter)
}
$result | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $evidencePath 'upgrade-result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 6
if ($passed -and (Test-Path -LiteralPath $isolatedCodex)) {
    $resolvedCodexHome = (Resolve-Path -LiteralPath $isolatedCodex).Path
    $allowedCodexPrefix = [IO.Path]::GetFullPath($codexTestParent).TrimEnd('\') + '\'
    if (-not $resolvedCodexHome.StartsWith(
            $allowedCodexPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedCodexHome) -notlike 'v1.2.1-*') {
        throw "Refusing to clean unexpected Codex home: $resolvedCodexHome"
    }
    Remove-Item -LiteralPath $resolvedCodexHome -Recurse -Force
}
if (-not $passed) { exit 1 }
