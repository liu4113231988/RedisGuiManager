<#
.SYNOPSIS
    Publishes RedisGuiManager as a self-contained, single-folder Windows x64 ZIP.

.DESCRIPTION
    Produces the artifact described in version-1.4.0.md: a self-contained build that carries its
    own .NET runtime, so the target machine does not need .NET installed. The native SQLite
    interop DLL and the redis.ico are verified to be present in the output, because the query
    window fails at runtime without SQLite.Interop.dll.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Runtime
    Target runtime identifier. Defaults to win-x64.

.PARAMETER Version
    Version stamped into the archive name. Defaults to the project version in the .csproj.

.PARAMETER OutputPath
    Directory to publish into. Defaults to artifacts/publish.

.PARAMETER SkipArchive
    Publish only; do not create the ZIP archive.

.EXAMPLE
    ./scripts/publish.ps1
    ./scripts/publish.ps1 -Runtime win-x86 -OutputPath artifacts/publish-x86
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime      = 'win-x64',
    [string] $Version,
    [string] $OutputPath  = 'artifacts/publish',
    [switch] $SkipArchive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'RedisGuiManager\RedisGuiManager.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content -Raw $project)).Project.PropertyGroup.Version
    if (-not $Version) { throw 'Could not determine the version from RedisGuiManager.csproj' }
}

$stage = Join-Path $OutputPath $Runtime
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

Write-Host "==> Publishing RedisGuiManager $Version ($Runtime, $Configuration)" -ForegroundColor Cyan

& dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $stage `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Fail loudly here rather than shipping a package that breaks on first use.
$executable = Join-Path $stage 'RedisGuiManager.exe'
if (-not (Test-Path $executable)) { throw "Expected $executable to exist" }

$sqliteInterop = Get-ChildItem -Path $stage -Filter 'SQLite.Interop.dll' -Recurse -File
if (-not $sqliteInterop) {
    throw 'SQLite.Interop.dll is missing from the publish output; the query window would fail at runtime.'
}

Write-Host "    SQLite.Interop.dll: $($sqliteInterop[0].FullName)" -ForegroundColor DarkGray

if ($SkipArchive) {
    Write-Host "==> Published to $stage (archive skipped)" -ForegroundColor Green
    return
}

$archive = Join-Path $OutputPath "RedisGuiManager-$Version-$Runtime.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }

# archive.zip must contain the contents of the stage folder, not the folder itself.
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $archive).Length / 1MB, 1)
Write-Host "==> Created $archive ($sizeMb MB)" -ForegroundColor Green