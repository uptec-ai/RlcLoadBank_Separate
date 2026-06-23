<#
.SYNOPSIS
    Resolves the RLC Load Bank solution root, wherever the repo is cloned.

.DESCRIPTION
    The repo is shared via GitHub and may live under any absolute path. This
    script walks UP the directory tree from a starting path until it finds the
    solution file, and returns that directory's full path. Use it instead of
    hardcoding "C:\Project\1. RLC\...".

.PARAMETER StartPath
    Where to start the upward search. Defaults to this script's own folder, so
    `& .\scripts\Resolve-ProjectRoot.ps1` works from anywhere.

.PARAMETER SolutionName
    Solution file to look for. Defaults to RLC_LoadBank_SeparateVer.sln; falls
    back to any single *.sln found on the way up.

.EXAMPLE
    $root = & ".\scripts\Resolve-ProjectRoot.ps1"
    dotnet build (Join-Path $root 'RLC_LoadBank_SeparateVer.sln')

.EXAMPLE
    # From an arbitrary working directory:
    $root = & "$PSScriptRoot\Resolve-ProjectRoot.ps1" -StartPath (Get-Location)
.OUTPUTS
    System.String  - absolute path of the directory containing the .sln
#>
[CmdletBinding()]
param(
    [string]$StartPath = $PSScriptRoot,
    [string]$SolutionName = 'RLC_LoadBank_SeparateVer.sln'
)

if ([string]::IsNullOrWhiteSpace($StartPath)) { $StartPath = (Get-Location).Path }

$dir = Get-Item -LiteralPath $StartPath -ErrorAction Stop
if (-not $dir.PSIsContainer) { $dir = $dir.Directory }

while ($null -ne $dir) {
    # 1) preferred: the named solution
    $named = Join-Path $dir.FullName $SolutionName
    if (Test-Path -LiteralPath $named) { return $dir.FullName }

    # 2) fallback: any single .sln in this directory
    $sln = Get-ChildItem -LiteralPath $dir.FullName -Filter '*.sln' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($sln) { return $dir.FullName }

    $dir = $dir.Parent
}

throw "Solution root not found: no '$SolutionName' (or any *.sln) at or above '$StartPath'."
