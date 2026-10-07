<#
.SYNOPSIS
    Step 3: build the texture pack in work\pack\ and optionally deploy it into the game.

.DESCRIPTION
    Textures are written as BC7 .dds files with mipmaps, so the game loads them without stalling.
    Per texture: work\overrides\<key>.png (hand-made, used as-is) beats work\upscaled\ (AI output,
    padding cropped and resized to -Scale x the original), which beats a plain Lanczos resize
    (normal maps and textures too big for the upscaler). Textures that aren't upscaled yet are left out.
    -Deploy mirrors the pack into <game>\BepInEx\plugins\GSOHDTextures\textures (removing stale files).
    Press F9 in-game to reload it without restarting.

.EXAMPLE
    .\tools\textures\pack.ps1 -Deploy
    .\tools\textures\pack.ps1 -Scale 4 -MaxSize 4096 -Deploy
    .\tools\textures\pack.ps1 -OnlyList 'D:\...\BepInEx\GSOHDTextures-seen.txt' -Deploy
#>
[CmdletBinding()]
param(
    [double]$Scale = 2,
    [int]$MaxSize = 4096,
    [int]$MaxInput = 2048,
    [int]$Jobs = 6,
    [string]$OnlyList,
    [switch]$Deploy,
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\build\GSOBuild.psm1') -Force
$py = Initialize-ToolsVenv
$work = Join-Path (Get-RepoRoot) 'work'

$packArgs = @('-I', (Join-Path $PSScriptRoot 'pack.py'), $work, '--scale', $Scale, '--max-size', $MaxSize, '--max-input', $MaxInput, '--jobs', $Jobs)
if ($OnlyList) { $packArgs += @('--only-list', (Resolve-Path $OnlyList).Path) }
Invoke-Checked $py $packArgs

if ($Deploy) {
    $GameDir = Resolve-GameDir $GameDir
    $dest = Join-Path (Get-PluginDir $GameDir) 'textures'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    robocopy (Join-Path $work 'pack') $dest *.dds *.png /MIR /NFL /NDL /NJH /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
    Write-Host "Deployed to $dest"
}
