---
name: harness
description: >-
  Set up "harness engineering" (Claude Code context engineering) for ANY
  codebase. Generates a slim, portable CLAUDE.md; path-scoped .claude/rules that
  load only when relevant files are touched; on-demand .claude/docs domain
  reference; a per-unit (View / route / screen / module / service) doc system
  that minimizes tokens; a portable project-root resolver; memory initialization;
  and optional agent teams. Use when the user asks to "set up the harness",
  "configure harness engineering", "harness 구성/구성해줘", "build a harness for
  this project", or wants Claude Code project context, rules, or memory scaffolding.
---

# Harness — project context-engineering factory

This skill scaffolds a **token-efficient, portable Claude Code harness** for a
codebase. The goal: a *small* always-on context plus *path-scoped* detail and
*on-demand* reference, so Claude has exactly the right context for the file in
front of it and nothing more.

It is technology-agnostic. The "unit" abstraction adapts to the stack:
- WPF / desktop MVVM → **per View** (+ ViewModel)
- Web frontend → **per route / page / feature**
- Backend / services → **per service / module / bounded context**
- Library → **per public module**

## When to use
Invoke when the user wants to (re)establish project context for Claude Code:
"set up / configure the harness", "harness 구성", "build a harness", "set up
CLAUDE.md and rules", "make per-screen docs to save tokens", or to add a new
per-unit doc to an existing harness.

## Inputs to gather first
1. **Project root** — find the build/anchor file (`*.sln`, `package.json`,
   `pyproject.toml`, `go.mod`, `Cargo.toml`, `.git`). The harness must be
   **portable**: never hardcode an absolute path; resolve the root from this
   anchor (see `references/templates.md` → root resolvers).
2. **Tech stack** — read the anchor + dependency manifests for real versions
   (do not trust prose if the manifest disagrees; flag mismatches to the user).
3. **Domain** — any spec docs (`.docx`, `.pdf`, `.md`), a README, or the user's
   description. Extract a concise English domain model.
4. **Units** — the list of Views / routes / modules (existing + planned).
5. **Constraints** — workspace boundary, language for docs, shell, comms, etc.
   Ask the user only for decisions you cannot derive (see "Ask, don't assume").

## Procedure
1. **Confirm scope** with the user for anything ambiguous: where the reusable
   skill lives (workspace vs `~/.claude`), version conflicts, and how many units
   to scaffold now. Prefer 2–4 crisp `AskUserQuestion` items over a long quiz.
2. **Root resolver** — write a `scripts/Resolve-ProjectRoot.*` that walks up to
   the anchor file. Use it in all build/run commands instead of literal paths.
3. **`.claude/CLAUDE.md`** (always-on, **keep < 200 lines**): what the project is,
   tech stack (from manifests), portable-root instructions, build/run commands,
   layout, core conventions, the per-unit doc workflow, boundaries, and a short
   domain quick-facts block that points to `.claude/docs/`. See
   `references/context-engineering.md` and `references/templates.md`.
4. **`.claude/rules/`** — one file per cross-cutting concern (e.g. data access,
   comms protocol, charting, testing, security). Give each a `paths:`
   front-matter glob so it loads **only** when matching files are opened. Rules
   with no `paths:` load every session — use that sparingly.
5. **Per-unit docs** — put `.claude/templates/<unit>-doc-template.md` and create
   one `.claude/rules/<units>/<name>.md` per unit, each `paths:`-scoped to that
   unit's files so editing one unit loads only its doc. Document the workflow in
   CLAUDE.md so future units follow it.
6. **`.claude/docs/`** — distill heavy/external specs into concise English
   reference, **on-demand** (not auto-loaded). Add a `README.md` index. Keep the
   original source files as the cited source of truth.
7. **Memory** — initialize the auto-memory `MEMORY.md` index and seed
   `user` / `project` / `feedback` memories for non-obvious, durable facts
   (see `references/context-engineering.md` → memory). Don't duplicate what
   CLAUDE.md or the code already states.
8. **(Optional) Agent teams** — if the work benefits from multi-agent
   orchestration, generate `.claude/agents/*.md` and supporting skills using a
   pattern from `references/architecture-patterns.md`.
9. **Verify** — list every file created, confirm CLAUDE.md is under the size
   budget, and confirm no path is hardcoded. Tell the user how to invoke/extend
   the harness.

## Token-minimization principles (the whole point)
- **Always-on = minimal.** Only CLAUDE.md (and any unconditional rule) loads
  every session. Keep CLAUDE.md tight; push specifics down.
- **Path-scoped = on touch.** `.claude/rules/*.md` with `paths:` load only when a
  matching file is opened. One unit ↔ one scoped doc ↔ loads only for that unit.
- **On-demand = read when needed.** `.claude/docs/**` are referenced by path and
  read deliberately, not injected at startup.
- **Manifests over prose.** Pull versions/commands from build files; flag drift.
- **Portable over absolute.** Resolve the root; never bake in a machine path.

## Ask, don't assume
Only ask the user for genuine decisions: skill install location (workspace vs
global, when global means leaving the workspace), conflicting versions, doc
language, and how many units to scaffold now. Derive everything else.

## Outputs
`scripts/Resolve-ProjectRoot.*`, `.claude/CLAUDE.md`, `.claude/rules/**`,
`.claude/rules/<units>/**`, `.claude/templates/**`, `.claude/docs/**`,
memory `MEMORY.md` (+ seed memories), and optionally `.claude/agents/**`.

## References (read when you need the detail)
- `references/context-engineering.md` — CLAUDE.md sizing, path-scoped rules,
  imports, memory hierarchy, what survives compaction (distilled from the
  official Claude Code memory docs).
- `references/architecture-patterns.md` — the six agent-team patterns and when
  to pick each.
- `references/templates.md` — copy-paste boilerplate: CLAUDE.md skeleton, a
  path-scoped rule, a per-unit doc, and root resolvers for PowerShell and bash.

## How to invoke (tell the user)
- Type `/harness` (or ask: "harness 구성해줘" / "set up the harness").
- Add specifics to skip the interview, e.g.:
  - "Set up the harness for this .NET WPF app; units are Views; docs in English."
  - "Add a per-View doc for the Metering screen."
  - "Re-slim CLAUDE.md and move the Modbus details into a path-scoped rule."
