#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$false)]
    [string]$CsprojPath = "OmmerCSharp/Ommer/Ommer.csproj",
    
    [Parameter(Mandatory=$false)]
    [switch]$WithCommitSuffix
)

# Get current directory
$rootPath = Split-Path -Parent $PSScriptRoot

# Read current version from csproj
$csprojFullPath = Join-Path $rootPath $CsprojPath
if (-not (Test-Path $csprojFullPath)) {
    Write-Error "Could not find csproj file at: $csprojFullPath"
    exit 1
}

$csprojContent = Get-Content $csprojFullPath -Raw
$versionMatch = [regex]::Match($csprojContent, '<Version>(.*?)<\/Version>')

if (-not $versionMatch.Success) {
    Write-Error "Could not find <Version> tag in csproj file"
    exit 1
}

$version = $versionMatch.Groups[1].Value

if ($WithCommitSuffix) {
    # Check if we're in a git repository and get short commit hash
    if (Test-Path (Join-Path $rootPath ".git")) {
        $shortHash = git rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $version = "$version-dev-$shortHash"
        }
    }
}

Write-Output $version
