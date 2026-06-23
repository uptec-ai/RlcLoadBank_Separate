# Harness templates (copy-paste boilerplate)

Generic starting points. Replace `<...>` placeholders. Keep the produced
CLAUDE.md under ~200 lines.

## CLAUDE.md skeleton
```markdown
# <Project> — Project Instructions

<one-paragraph: what this is, who uses it, where heavy reference lives>

## Tech stack (source of truth = the manifest)
- IDE/runtime/framework, app type
- Key libraries with REAL versions (from the manifest; flag prose mismatches)
- Shell / comms / db

## Project root — portable, never hardcode paths
This repo may be cloned to any folder. Resolve the root from <anchor file>:
`$root = & ".\scripts\Resolve-ProjectRoot.ps1"` (walks up to the anchor).
If the cwd is not under that root, re-resolve before building.

## Build / run
<commands that use $root, not absolute paths>

## Layout
<top-level folders and what lives where>

## Conventions
<verifiable rules: naming, patterns, "always do X">

## Per-unit docs (token-minimized)
One `.md` per <View/route/module> under `.claude/rules/<units>/`, scoped with
`paths:` so only the active unit's doc loads. New unit → copy the template,
set `paths:`, fill it in.

## Boundaries
- Stay inside the workspace; ask before touching files outside it.
- Docs under `.claude/**` are written in <language>.
- Never edit build artifacts.

## Domain quick-facts (details in `.claude/docs/`)
<5–8 bullets; link out, don't inline the whole spec>
```

## Path-scoped rule
```markdown
---
paths:
  - "<glob/**/*.ext>"
---
# <Concern> rules
Loads only while editing matching files.
- <verifiable guidance>
```

## Per-unit doc template
```markdown
<!-- COPY to .claude/rules/<units>/<name>.md; the paths block scopes loading. -->
---
paths:
  - "<glob to this unit's files>"
---
# <Unit>: <Name>
## Purpose
## Status
## Owner module / ViewModel / controller
## Data & external I/O (endpoints, tags, tables)
## UI / API surface
## Gotchas / rules
## Related docs (.claude/docs/...)
```

## Root resolver — PowerShell
```powershell
[CmdletBinding()]
param([string]$StartPath = $PSScriptRoot, [string]$Anchor = '*.sln')
if ([string]::IsNullOrWhiteSpace($StartPath)) { $StartPath = (Get-Location).Path }
$dir = Get-Item -LiteralPath $StartPath
if (-not $dir.PSIsContainer) { $dir = $dir.Directory }
while ($null -ne $dir) {
    if (Get-ChildItem -LiteralPath $dir.FullName -Filter $Anchor -File -ErrorAction SilentlyContinue | Select-Object -First 1) {
        return $dir.FullName
    }
    $dir = $dir.Parent
}
throw "Anchor '$Anchor' not found at or above '$StartPath'."
```

## Root resolver — bash
```bash
#!/usr/bin/env bash
# Walks up to the first dir containing the anchor (default: a .sln; or .git, package.json...)
resolve_root() {
  local dir="${1:-$PWD}" anchor="${2:-*.sln}"
  dir="$(cd "$dir" && pwd)"
  while [ -n "$dir" ]; do
    if compgen -G "$dir/$anchor" >/dev/null 2>&1; then echo "$dir"; return 0; fi
    [ "$dir" = "/" ] && break
    dir="$(dirname "$dir")"
  done
  echo "anchor '$anchor' not found above ${1:-$PWD}" >&2; return 1
}
resolve_root "$@"
```

## Promote a workspace skill to global (reuse everywhere)
```powershell
# From the repo root — makes /<skill> available in all projects.
Copy-Item ".\.claude\skills\<skill>" "$HOME\.claude\skills\" -Recurse -Force
```
(Writing under `~/.claude` leaves the workspace — get the user's OK first.)
