# Agent-team architecture patterns

Optional layer. Use these only when a task genuinely benefits from multiple
coordinated agents; otherwise the context harness (CLAUDE.md + rules + docs +
memory) is enough. Six patterns, with when to pick each. (Based on the harness
project's team taxonomy.)

| # | Pattern | Shape | Pick when |
| --- | --- | --- | --- |
| 1 | **Pipeline** | A → B → C, each stage feeds the next | sequential, dependent stages (analyze → build → verify) |
| 2 | **Fan-out / Fan-in** | split → N parallel workers → merge | independent sub-tasks over a known work-list (per file, per module) |
| 3 | **Expert Pool** | route each item to the best-fit specialist | heterogeneous items needing different expertise |
| 4 | **Generate–Validate** | producer + adversarial checker / quality gate | correctness matters; want a second, skeptical pass |
| 5 | **Supervisor** | one coordinator dispatches & integrates | central planning + dynamic assignment |
| 6 | **Hierarchical delegation** | recursive top-down decomposition | large scope that must be broken down repeatedly |

## Generated team output (when used)
```
.claude/
├── agents/
│   ├── analyst.md      # reads/maps the domain
│   ├── builder.md      # implements
│   └── qa.md           # adversarially validates
└── skills/
    ├── analyze/SKILL.md
    └── build/SKILL.md
```

## Writing an agent definition (`.claude/agents/<name>.md`)
Front-matter: `name`, `description` (when to use — be specific so it's selected
correctly), optional `tools` (least privilege), optional `model`. Body: the
agent's role, inputs, method, and the exact shape of what it returns (its final
message is the result, so specify the output contract).

## Orchestration tips
- Prefer a **pipeline** over a barrier (`parallel`) unless a stage truly needs
  all prior results at once — barriers waste the fast workers' wall-clock.
- For unknown-size discovery (bugs, edge cases), loop finders **until K dry
  rounds**, dedup against everything seen, then validate (Generate–Validate).
- Give validators **distinct lenses** (correctness / security / perf / repro)
  rather than N identical checkers.
- Log anything you cap (top-N, no-retry, sampling) so coverage isn't overstated.

## Evolve
After a generated harness is used for real work, capture the delta between the
initial design and what production needed, and fold those lessons back into the
project's rules/docs (and into seed memories) so the next setup starts better.
