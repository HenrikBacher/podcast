#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$BumpType,
    
    [Parameter(Mandatory=$false)]
    [string]$CsprojPath = "OmmerCSharp/Ommer/Ommer.csproj"
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

$currentVersion = $versionMatch.Groups[1].Value
Write-Host "Current version: $currentVersion"

# Parse version parts
$versionParts = $currentVersion.Split('.')
if ($versionParts.Length -ne 3) {
    Write-Error "Version must be in format Major.Minor.Patch"
    exit 1
}

$major = [int]$versionParts[0]
$minor = [int]$versionParts[1]
$patch = [int]$versionParts[2]

# Bump version based on type
switch ($BumpType) {
    "major" {
        $major++
        $minor = 0
        $patch = 0
    }
    "minor" {
        $minor++
        $patch = 0
    }
    "patch" {
        $patch++
    }
}

$newVersion = "$major.$minor.$patch"
Write-Host "New version: $newVersion"

# Update csproj file
$newCsprojContent = $csprojContent -replace '<Version>.*?<\/Version>', "<Version>$newVersion</Version>"
Set-Content $csprojFullPath $newCsprojContent -NoNewline

Write-Host "Version bumped from $currentVersion to $newVersion in $CsprojPath" -ForegroundColor Green

# Check if we're in a git repository
if (Test-Path (Join-Path $rootPath ".git")) {
    $gitStatus = git status --porcelain 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nGit commands you might want to run:" -ForegroundColor Yellow
        Write-Host "  git add $CsprojPath" -ForegroundColor Cyan
        Write-Host "  git commit -m `"Bump version to $newVersion`"" -ForegroundColor Cyan
        Write-Host "  git tag v$newVersion" -ForegroundColor Cyan
    }
}
