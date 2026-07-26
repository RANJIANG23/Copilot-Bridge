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
$codexTestParent = Join-Path $env:LOCALAPPDATA 'CopilotBridge-Phase40'
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
    consultationPolicy = 'CodexMayConsult'
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

function Install-AppOnlyPackage([string]$Package, [string]$InstallDirectory) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $Package 'Install-CopilotBridge.ps1') `
        -InstallDirectory $InstallDirectory `
        -StartMenuDirectory $startMenu `
        -SkipCodexPlugin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "App-only installer failed: $Package" }
}

function Uninstall-Package([string]$InstallDirectory) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $InstallDirectory 'Uninstall-CopilotBridge.ps1') `
        -StartMenuDirectory $startMenu | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Uninstaller failed: $InstallDirectory" }
}

function Uninstall-AppOnlyPackage([string]$InstallDirectory) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $InstallDirectory 'Uninstall-CopilotBridge.ps1') `
        -InstallDirectory $InstallDirectory `
        -StartMenuDirectory $startMenu `
        -SkipCodexPlugin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "App-only uninstaller failed: $InstallDirectory" }
}

function Invoke-PackagedMcp([string]$InstallDirectory, [string]$TranscriptName) {
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
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"generic-stdio-phase40","version":"1"}}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"copilot_bridge_status","arguments":{}}}')
    $process.StandardInput.WriteLine(
        '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_conversations","arguments":{}}}')
    $process.StandardInput.Flush()
    $outputLines = [Collections.Generic.List[string]]::new()
    $received = [Collections.Generic.HashSet[int]]::new()
    while ($received.Count -lt 4 -and $outputLines.Count -lt 10) {
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
    Set-Content -LiteralPath (Join-Path $evidencePath "$TranscriptName-stdout.jsonl") `
        -Value $output -Encoding utf8
    Set-Content -LiteralPath (Join-Path $evidencePath "$TranscriptName-stderr.txt") `
        -Value $errorText -Encoding utf8
    if ($process.ExitCode -ne 0) { throw "Packaged MCP failed: $errorText" }
    $messages = @($output -split "`r?`n" |
        Where-Object { $_.Trim() } |
        ForEach-Object { $_ | ConvertFrom-Json })
    $toolNames = @(($messages | Where-Object id -eq 2).result.tools |
        ForEach-Object name | Sort-Object)
    $expectedToolNames = @(
        'consult_copilot',
        'copilot_bridge_status',
        'read_conversation',
        'search_conversations')
    $initialize = $messages | Where-Object id -eq 1
    $status = $messages | Where-Object id -eq 3
    $statusContent = $status.result.structuredContent
    $statusRead = $null -ne $statusContent -and
        $null -ne $statusContent.PSObject.Properties['consultationPolicy'] -and
        $status.result.isError -ne $true
    $search = $messages | Where-Object id -eq 4
    $searchContent = $search.result.structuredContent
    $searchRead = $null -ne $searchContent -and
        $null -ne $searchContent.PSObject.Properties['results'] -and
        $search.result.isError -ne $true
    $searchResults = if ($searchRead) { @($searchContent.results) } else { @() }
    [pscustomobject]@{
        ToolNames = $toolNames
        ToolSetMatches = (@(Compare-Object $expectedToolNames $toolNames).Count -eq 0)
        SearchRead = $searchRead
        SearchResultCount = $searchResults.Count
        StatusRead = $statusRead
        StatusPolicy = if ($statusRead) { [string]$statusContent.consultationPolicy } else { $null }
        AgentNeutralInstructions = (
            [string]$initialize.result.instructions -like '*calling agent*' -and
            $output -like '*agent_auto*')
    }
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
    $mcp = Invoke-PackagedMcp $installDirectory 'plugin-mcp'
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

    $appOnlyInstallDirectory = Join-Path $isolatedLocal 'Programs\CopilotBridge-AppOnly'
    Install-AppOnlyPackage $candidatePackage $appOnlyInstallDirectory
    $appOnlyVersion = (Get-Item -LiteralPath `
        (Join-Path $appOnlyInstallDirectory 'CopilotBridge.exe')).VersionInfo.ProductVersion
    $appOnlyGuideInstalled = Test-Path -LiteralPath `
        (Join-Path $appOnlyInstallDirectory 'MCP-CLIENTS.md') -PathType Leaf
    $appOnlyMcp = Invoke-PackagedMcp $appOnlyInstallDirectory 'app-only-mcp'
    $userDataAfterAppOnlyMcp = Get-TreeHash $settingsDirectory
    Uninstall-AppOnlyPackage $appOnlyInstallDirectory
    $appOnlyRemoved = -not (Test-Path -LiteralPath $appOnlyInstallDirectory)
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $env:CODEX_HOME = $originalCodexHome
    $env:PATH = $originalPath
}
$hostConfigAfter = if (Test-Path -LiteralPath $hostConfig) {
    (Get-FileHash -LiteralPath $hostConfig -Algorithm SHA256).Hash
} else { 'missing' }

$passed = $previousVersion -like '1.3.1*' -and
    $candidateVersion -like '1.4.0*' -and
    $rollbackVersion -like '1.3.1*' -and
    $appOnlyVersion -like '1.4.0*' -and
    $mcp.ToolNames.Count -eq 4 -and
    $mcp.ToolSetMatches -and
    $mcp.StatusRead -and
    $mcp.StatusPolicy -eq 'codex_may_consult' -and
    $mcp.AgentNeutralInstructions -and
    $mcp.SearchRead -and
    $mcp.SearchResultCount -eq 0 -and
    $appOnlyMcp.ToolNames.Count -eq 4 -and
    $appOnlyMcp.ToolSetMatches -and
    $appOnlyMcp.StatusRead -and
    $appOnlyMcp.StatusPolicy -eq 'codex_may_consult' -and
    $appOnlyMcp.AgentNeutralInstructions -and
    $appOnlyMcp.SearchRead -and
    $appOnlyMcp.SearchResultCount -eq 0 -and
    $appOnlyGuideInstalled -and
    $appOnlyRemoved -and
    $userDataBefore -eq $userDataAfterMcp -and
    $userDataBefore -eq $userDataAfterAppOnlyMcp -and
    $uninstallPreservedData -and
    $pluginCountAfterUninstall -eq 0 -and
    $marketplaceCountAfterUninstall -eq 0 -and
    $plugin.version -eq '1.4.0' -and
    $hostConfigBefore -eq $hostConfigAfter
$result = [ordered]@{
    result = if ($passed) { 'passed' } else { 'failed' }
    evidenceRoot = $evidencePath
    previousVersion = $previousVersion
    candidateVersion = $candidateVersion
    rollbackVersion = $rollbackVersion
    appOnlyVersion = $appOnlyVersion
    pluginVersion = $plugin.version
    mcpTools = $mcp.ToolNames
    mcpToolSetMatches = $mcp.ToolSetMatches
    genericClientName = 'generic-stdio-phase40'
    statusRead = $mcp.StatusRead
    statusPolicy = $mcp.StatusPolicy
    agentNeutralInstructions = $mcp.AgentNeutralInstructions
    searchRead = $mcp.SearchRead
    legacyOffSearchResults = $mcp.SearchResultCount
    appOnlyMcpTools = $appOnlyMcp.ToolNames
    appOnlyMcpToolSetMatches = $appOnlyMcp.ToolSetMatches
    appOnlyStatusRead = $appOnlyMcp.StatusRead
    appOnlyStatusPolicy = $appOnlyMcp.StatusPolicy
    appOnlyAgentNeutralInstructions = $appOnlyMcp.AgentNeutralInstructions
    appOnlySearchRead = $appOnlyMcp.SearchRead
    appOnlyLegacyOffSearchResults = $appOnlyMcp.SearchResultCount
    appOnlyGuideInstalled = $appOnlyGuideInstalled
    appOnlyRemoved = $appOnlyRemoved
    userDataPreserved = ($userDataBefore -eq $userDataAfterMcp)
    appOnlyUserDataPreserved = ($userDataBefore -eq $userDataAfterAppOnlyMcp)
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
        [IO.Path]::GetFileName($resolvedCodexHome) -notlike 'v1.4.0-*') {
        throw "Refusing to clean unexpected Codex home: $resolvedCodexHome"
    }
    Remove-Item -LiteralPath $resolvedCodexHome -Recurse -Force
}
if (-not $passed) { exit 1 }
