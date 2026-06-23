<!--
  View-doc template. COPY this file to .claude/rules/views/<view-name>.md when you
  create a new View, then fill in every section. The `paths:` block is what makes
  this doc load ONLY while that View's files are open (token-minimized).

  This template lives under .claude/templates/ (NOT .claude/rules/) on purpose, so
  it is never auto-loaded as a rule.
-->
---
paths:
  - "**/Views/<ViewName>.xaml"
  - "**/Views/<ViewName>.xaml.cs"
  - "**/ViewModels/<ViewName>Model.cs"
---

# View: <ViewName>

## Purpose
One or two sentences: what this screen does and who uses it.

## Status
Not started | In progress | Done — and a one-line note on current state.

## ViewModel
- `<ViewName>Model : DevExpress.Mvvm.ViewModelBase`
- Key bound properties:
- Commands (with their interlock / CanExecute conditions):

## Data & Modbus tags
- PLC(s) involved (PNL-1 / PNL-2 / PNL-3):
- Read (`*_FB`) tags shown:
- Write (`*_CMD`) tags issued, and the interlocks that gate them:
- Measurement items shown (see `.claude/docs/measurement-items.md`):

## UI / DevExpress
- Controls used, layout notes, theme considerations.

## Gotchas / rules
- Anything non-obvious (sequencing, Local/Remote behavior, FB↔CMD mismatch alarms).

## Related docs
- `.claude/docs/...` sections this View depends on.
