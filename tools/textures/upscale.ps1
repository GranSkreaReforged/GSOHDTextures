<#
.SYNOPSIS
    Step 2: AI-upscale dumped textures with Real-ESRGAN into work\upscaled\.

.DESCRIPTION
    Only textures without an existing work\upscaled\<key>.png are processed, so it can be stopped
    and resumed. Normal maps and textures over -MaxInput are skipped here; pack.ps1 resizes them.
    Real-ESRGAN (pinned, hash-checked) is downloaded to .cache\ on first use. It needs a Vulkan GPU.

.EXAMPLE
    .\tools\textures\upscale.ps1
    .\tools\textures\upscale.ps1 -OnlyList "$env:GSO_GAME_DIR\BepInEx\GSOHDTextures-seen.txt"
    .\tools\textures\upscale.ps1 -Model realesrgan-x4plus-anime -Tile 256   # stylised art / low VRAM
#>
[CmdletBinding()]
param(
    [ValidateSet('realesrgan-x4plus', 'realesrgan-x4plus-anime', 'realesr-animevideov3')]
    [string]$Model = 'realesrgan-x4plus',
    [int]$MaxInput = 2048,
    [string]$OnlyList,
    [int]$Tile = 0,
    [string]$Gpu
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\build\GSOBuild.psm1') -Force
$py = Initialize-ToolsVenv
$exe = Get-RealEsrgan
$work = Join-Path (Get-RepoRoot) 'work'
if (-not (Test-Path (Join-Path $work 'index.json'))) { throw 'No work\index.json yet. Run tools\textures\dump.ps1 first.' }

$prepArgs = @('-I', (Join-Path $PSScriptRoot 'prep.py'), $work, '--max-input', $MaxInput)
if ($OnlyList) { $prepArgs += @('--only-list', (Resolve-Path $OnlyList).Path) }
Invoke-Checked $py $prepArgs

$in = Join-Path $work 'upscale_in'
$out = Join-Path $work 'upscaled'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$count = @(Get-ChildItem $in -Filter *.png).Count
if ($count -eq 0) { Write-Host 'Nothing to upscale.'; return }

Write-Host "Upscaling $count textures with $Model (this can take a long time)..."
$esrganArgs = @('-i', $in, '-o', $out, '-n', $Model, '-s', '4', '-f', 'png', '-t', $Tile, '-m', (Join-Path (Split-Path $exe) 'models'))
if ($Gpu) { $esrganArgs += @('-g', $Gpu) }
Invoke-Checked $exe $esrganArgs
Remove-Item $in -Recurse -Force
Write-Host "Done. Next: tools\textures\pack.ps1 -Deploy"
