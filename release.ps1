<#
.SYNOPSIS
    Cut a release: bump version, build Release, package zips + checksums, commit and tag.

.DESCRIPTION
    Produces in dist\:
      GSOHDTextures-<ver>.zip               plugin only (for players who already have BepInEx)
      GSOHDTextures-<ver>-with-BepInEx.zip  extract into the game folder
      SHA256SUMS.txt
    Neither zip contains textures or other game files. A texture pack is built locally with
    tools\textures (see README). Nothing is pushed; push the commit and tag yourself when happy.

.EXAMPLE
    .\release.ps1 -Version 0.2.0
    .\release.ps1 -Version 0.2.0 -DryRun     # package only, no version bump/commit/tag
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$GameDir,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'build\GSOBuild.psm1') -Force
$root = Get-RepoRoot
Set-Location $root

$tag = "v$Version"
if (-not $DryRun) {
    if (git status --porcelain) { throw 'Working tree is not clean. Commit or stash first.' }
    if (git tag --list $tag) { throw "Tag $tag already exists." }
}

$GameDir = Resolve-GameDir $GameDir
Save-GameDir $GameDir
Get-BepInExCore | Out-Null   # BepInEx's DLLs for compiling, from the pinned zip

$changelog = Join-Path $root 'CHANGELOG.md'
$previousVersion = Get-ProjectVersion
$originalChangelog = Get-Content $changelog -Raw

try {
    if (-not $DryRun) {
        Set-ProjectVersion $Version
        $date = Get-Date -Format 'yyyy-MM-dd'
        $updated = $originalChangelog -replace '(?m)^## \[Unreleased\]\s*$', "## [Unreleased]`n`n## [$Version] - $date"
        if ($updated -eq $originalChangelog) { throw 'CHANGELOG.md has no "## [Unreleased]" heading.' }
        [IO.File]::WriteAllText($changelog, $updated)
    }

    $project = Join-Path $root 'src\GSOHDTextures\GSOHDTextures.csproj'
    $out = Join-Path $root 'artifacts\release'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Invoke-Checked dotnet @('build', $project, '-c', 'Release', '-nologo', "-p:Version=$Version", '-o', $out)

    $dist = Join-Path $root 'dist'
    $staging = Join-Path $root 'artifacts\staging'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

    # Layout mirrors the game folder so either zip can be extracted straight into it.
    $plugin = Join-Path $staging 'plugin'
    $pluginDir = Join-Path $plugin 'BepInEx\plugins\GSOHDTextures'
    New-Item -ItemType Directory -Force -Path (Join-Path $pluginDir 'textures') | Out-Null
    Copy-Item (Join-Path $out 'GSOHDTextures.dll') $pluginDir
    Copy-Item (Join-Path $root 'README.md') (Join-Path $pluginDir 'README.md')
    Copy-Item $changelog (Join-Path $pluginDir 'CHANGELOG.md')
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $pluginDir 'LICENSE')
    Copy-Item (Join-Path $root 'docs\INSTALL.md') (Join-Path $pluginDir 'INSTALL.md')
    Set-Content -Path (Join-Path $pluginDir 'textures\PUT_TEXTURES_HERE.txt') -Value 'Replacement textures go in this folder as <name>__<width>x<height>.dds (built by tools\textures\pack.ps1) or .png. See README.md.'

    $full = Join-Path $staging 'full'
    Expand-Archive -Path (Get-BepInExZip) -DestinationPath $full
    Copy-Item (Join-Path $plugin '*') $full -Recurse -Force

    $zipPlugin = Join-Path $dist "GSOHDTextures-$Version.zip"
    $zipFull = Join-Path $dist "GSOHDTextures-$Version-with-BepInEx.zip"
    foreach ($z in $zipPlugin, $zipFull) { if (Test-Path $z) { Remove-Item $z } }
    Compress-Archive -Path (Join-Path $plugin '*') -DestinationPath $zipPlugin
    Compress-Archive -Path (Join-Path $full '*') -DestinationPath $zipFull

    $sums = foreach ($z in $zipPlugin, $zipFull) {
        "{0}  {1}" -f (Get-FileHash -Algorithm SHA256 $z).Hash.ToLowerInvariant(), (Split-Path $z -Leaf)
    }
    $sums | Set-Content (Join-Path $dist 'SHA256SUMS.txt')
}
catch {
    if (-not $DryRun) {
        Set-ProjectVersion $previousVersion
        [IO.File]::WriteAllText($changelog, $originalChangelog)
    }
    throw
}

if (-not $DryRun) {
    Invoke-Checked git @('add', 'Directory.Build.props', 'CHANGELOG.md')
    Invoke-Checked git @('commit', '-m', "Release $tag")
    Invoke-Checked git @('tag', '-a', $tag, '-m', "GSO HD Textures $Version")
}

Write-Host ''
Write-Host "Packaged $Version (BepInEx $BepInExVersion):" -ForegroundColor Green
Get-Content (Join-Path $dist 'SHA256SUMS.txt') | ForEach-Object { Write-Host "  $_" }
if (-not $DryRun) {
    Write-Host "Committed and tagged $tag. Publish with: git push --follow-tags"
}
