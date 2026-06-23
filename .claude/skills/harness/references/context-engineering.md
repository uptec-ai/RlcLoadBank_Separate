# Context engineering reference

Distilled from the official Claude Code memory docs
(https://code.claude.com/docs/ko/memory) plus practical harness conventions.
These are the rules the `harness` skill applies.

## Two memory systems
| | CLAUDE.md | Auto-memory |
| --- | --- | --- |
| Written by | the user / harness | Claude |
| Holds | instructions & rules | learnings & patterns |
| Scope | project / user / org | per repository (shared across worktrees) |
| Loaded | every session, in full | `MEMORY.md` first 200 lines / 25 KB each session |
| Use for | standards, layout, workflows, "always do X" | build cmds, debug insights, discovered prefs |

Both are **context, not hard enforcement**. To *block* an action regardless of
Claude's choice, use a `PreToolUse` hook, not CLAUDE.md.

## CLAUDE.md placement & load order (broad → specific)
1. Managed policy: `/Library/Application Support/ClaudeCode/CLAUDE.md` (macOS),
   `/etc/claude-code/CLAUDE.md` (Linux/WSL), `C:\Program Files\ClaudeCode\CLAUDE.md` (Win).
2. User: `~/.claude/CLAUDE.md` (all your projects).
3. Project: `./CLAUDE.md` **or** `./.claude/CLAUDE.md` (shared via source control).
4. Local: `./CLAUDE.local.md` (gitignored, personal, current project).

Files up the directory tree load in full at startup (root → cwd order).
Subdirectory CLAUDE.md files load lazily when Claude reads files there. Project
root CLAUDE.md is re-injected after `/compact`; nested ones are not.

## CLAUDE.md effectiveness
- **Size:** aim **< 200 lines**. Longer files cost context and reduce compliance.
- **Structure:** markdown headers + bullets; Claude scans structure like a reader.
- **Specificity:** verifiable instructions ("2-space indent", "run `npm test`
  before commit", "handlers live in `src/api/handlers/`"), not vague ones.
- **Consistency:** remove conflicting/stale instructions; conflicts make Claude
  pick arbitrarily.
- HTML comments (`<!-- ... -->`) at block level are stripped before injection —
  use them for maintainer notes without spending tokens.

## Imports
`@path/to/file` expands into context at startup (relative to the file doing the
import; up to 4 hops deep). Importing does **not** save startup tokens — the
imported file still loads. Use imports for organization, `.claude/rules/` for
conditional loading. `@AGENTS.md` lets one CLAUDE.md reuse an existing AGENTS.md.

## `.claude/rules/` — the token lever
- Markdown files under `.claude/rules/` (discovered recursively; subfolders OK).
- **No `paths:`** front-matter → loads every session (same priority as
  `.claude/CLAUDE.md`). Use rarely.
- **With `paths:`** front-matter → loads **only** when Claude opens a file
  matching the glob(s). This is how per-unit docs stay out of context until
  needed:
  ```markdown
  ---
  paths:
    - "src/api/**/*.ts"
    - "src/**/*.{ts,tsx}"
  ---
  # API rules
  - ...
  ```
- User-level rules: `~/.claude/rules/` (apply to all projects, lower priority
  than project rules). Symlinks are supported for sharing rule sets across repos.

## Auto-memory (Claude-written)
- Stored at `~/.claude/projects/<project>/memory/` (derived from the git repo, so
  all worktrees share it). Machine-local; not shared via cloud/repo.
- `MEMORY.md` is the index — only its first 200 lines / 25 KB load each session;
  topic files (`debugging.md`, etc.) are read on demand.
- Keep one durable fact per memory; classify as `user` / `feedback` / `project` /
  `reference`. Don't store what the repo/CLAUDE.md/git already records.

## What survives compaction
Project root CLAUDE.md is re-read from disk and re-injected after `/compact`.
Conversation-only instructions and nested CLAUDE.md are not auto-restored — put
anything that must persist into CLAUDE.md or memory.

## Harness layout this skill produces
```
<repo>/
├── CLAUDE.md  or  .claude/CLAUDE.md     # slim, always-on, portable root
├── .claude/
│   ├── rules/                           # path-scoped guidance
│   │   ├── <concern>.md                 # e.g. data.md, comms.md, charting.md
│   │   └── <units>/<name>.md            # one per View/route/module (paths-scoped)
│   ├── docs/                            # on-demand English domain reference
│   │   └── README.md
│   ├── templates/<unit>-doc-template.md # copy → rules/<units>/ for new units
│   ├── skills/<skill>/SKILL.md          # optional project skills
│   └── agents/<agent>.md                # optional agent team
└── scripts/Resolve-ProjectRoot.*        # portable root resolver
```
