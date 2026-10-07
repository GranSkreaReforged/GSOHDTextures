<#
.SYNOPSIS
    Step 1: export the game's textures to work\dump\<key>.png and index them in work\index.json.

.DESCRIPTION
    Reads YOUR game install with UnityPy. Lightmaps, reflection probes, font atlases and textures
    under -MinSize are indexed but not exported. Re-running only writes PNGs that are missing.
    Everything under work\ is proprietary game art: never commit or redistribute it.

.EXAMPLE
    .\tools\textures\dump.ps1
    .\tools\textures\dump.ps1 -Only Sword      # quick test on a few textures
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$Only = '',
    [int]$MinSize = 32
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\build\GSOBuild.psm1') -Force
$GameDir = Resolve-GameDir $GameDir
$py = Initialize-ToolsVenv
$work = Join-Path (Get-RepoRoot) 'work'

Invoke-Checked $py @('-I', (Join-Path $PSScriptRoot 'dump.py'), (Join-Path $GameDir 'GSO_Data'), $work, '--only', $Only, '--min-size', $MinSize)
