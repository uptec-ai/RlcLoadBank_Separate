<#
.SYNOPSIS
    Shows every worktree's branch, sync state, and uncommitted changes at a glance.

.DESCRIPTION
    For each entry in `git worktree list`:
      - Branch     : checked-out branch
      - Vs         : what Ahead/Behind is measured against
                     (feature branches vs main; the main row vs origin/main = push state)
      - Ahead      : commits NOT yet merged into the reference
                     (feature branch Ahead > 0 => merge up before editing main directly!)
      - Behind     : commits the branch is missing (fixed by "git merge main" = sync down)
      - Dirty      : uncommitted file count in that worktree

    Run this BEFORE editing directly in main and BEFORE starting a multi-task run
    (see CLAUDE.md -> "Worktree routing" / "Worktree rhythm").

.EXAMPLE
    & .\scripts\Get-WorktreeStatus.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = & (Join-Path $PSScriptRoot 'Resolve-ProjectRoot.ps1')

# ── parse `git worktree list --porcelain` ────────────────────────────────────
$entries = @()
$current = $null
$lines = @(git -C $root worktree list --porcelain)
foreach ($line in $lines) {
    if ($line -like 'worktree *') {
        if ($current) { $entries += $current }
        $current = @{ Path = $line.Substring(9) }
    }
    elseif ($line -like 'branch *') {
        $current.Branch = ($line.Substring(7) -replace '^refs/heads/', '')
    }
}
if ($current) { $entries += $current }

$hasOrigin = @(git -C $root remote) -contains 'origin'

# ── build rows ───────────────────────────────────────────────────────────────
$rows = foreach ($e in $entries) {
    $dirty = (git -C $e.Path status --porcelain | Measure-Object).Count
    if ($e.Branch -eq 'main') {
        if ($hasOrigin) {
            $vs     = 'origin/main'
            $ahead  = [int](git -C $root rev-list --count origin/main..main)
            $behind = [int](git -C $root rev-list --count main..origin/main)
        }
        else { $vs = '(no origin)'; $ahead = 0; $behind = 0 }
    }
    else {
        $vs     = 'main'
        $ahead  = [int](git -C $root rev-list --count "main..$($e.Branch)")
        $behind = [int](git -C $root rev-list --count "$($e.Branch)..main")
    }
    [pscustomobject]@{
        Worktree = Split-Path $e.Path -Leaf
        Branch   = $e.Branch
        Vs       = $vs
        Ahead    = $ahead
        Behind   = $behind
        Dirty    = $dirty
    }
}

$rows | Format-Table -AutoSize

# ── warnings ─────────────────────────────────────────────────────────────────
$unmerged = @($rows | Where-Object { $_.Branch -ne 'main' -and $_.Ahead -gt 0 })
$dirtyWts = @($rows | Where-Object { $_.Dirty -gt 0 })
$unpushed = @($rows | Where-Object { $_.Branch -eq 'main' -and $_.Ahead -gt 0 })

if ($unmerged.Count -eq 0 -and $dirtyWts.Count -eq 0) {
    Write-Host 'OK: all feature branches merged into main (Ahead 0) and clean - main-direct edits are safe.' -ForegroundColor Green
}
else {
    foreach ($w in $unmerged) {
        Write-Host ("WARN {0}: {1} unmerged commit(s) on {2} - merge up before editing main directly." -f $w.Worktree, $w.Ahead, $w.Branch) -ForegroundColor Yellow
    }
    foreach ($w in $dirtyWts) {
        Write-Host ("WARN {0}: {1} uncommitted file(s)." -f $w.Worktree, $w.Dirty) -ForegroundColor Yellow
    }
}
if ($unpushed.Count -gt 0) {
    Write-Host ("NOTE main: {0} commit(s) not pushed to origin/main." -f $unpushed[0].Ahead) -ForegroundColor Cyan
}
