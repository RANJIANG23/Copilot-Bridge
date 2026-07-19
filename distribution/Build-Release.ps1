[CmdletBinding()]
param(
    [string]$Version = '1.1.1'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageName = "CopilotBridge-$Version-win-x64"
$stage = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$project = Join-Path $repositoryRoot 'src\CopilotBridge\CopilotBridge.csproj'

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path (Join-Path $stage 'app') -Force | Out-Null

& dotnet test (Join-Path $repositoryRoot 'CopilotBridge.sln') -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'Release tests failed.'
}

& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o (Join-Path $stage 'app') `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw 'Self-contained publish failed.'
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'marketplace') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-CopilotBridge.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-CopilotBridge.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'INSTALL.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'TEAM-ROLLOUT.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'TROUBLESHOOTING.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NOTICE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage

$manifestPath = Join-Path $stage 'SHA256SUMS.txt'
$stagePrefixLength = $stage.TrimEnd('\').Length + 1
$hashLines = Get-ChildItem -LiteralPath $stage -File -Recurse |
    Where-Object FullName -ne $manifestPath |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stagePrefixLength).Replace('\', '/')
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
$hashLines | Set-Content -LiteralPath $manifestPath -Encoding utf8

Compress-Archive -LiteralPath $stage -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii

Write-Host "Release package: $zipPath"
Write-Host "SHA256: $zipHash"
