---
type: note
date: 2026-03-05
project: NexusSystemMonitor
tags: [nexus, index, hub]
---

# Nexus System Monitor — Project Hub

**Repository:** `github.com/brass458/nexus-system-monitor`
**Current version:** v0.1.6
**Tech stack:** C# 12 / .NET 8 / Avalonia UI 11.2.3 / MVVM + ReactiveUI
**Status:** Phases 1–16 complete · 9 releases shipped · Active development

---

## Design Philosophy

> [!important] The Apple Principle
> **If it doesn't immediately make sense to the user AND work, we don't ship it.**
>
> Every feature must be:
> - **Intuitive on first interaction** — no manual or tooltip required to understand what a control does
> - **Polished to completion** — no rough edges, no placeholder UI, no "good enough" layouts
> - **Actually functional** — not hidden behind a flag, not half-baked, not broken on any supported platform
>
> This is the bar Apple sets for its own tools. We hold Nexus to the same standard.

The goal is the **union** of features found in the best system tools — Process Lasso's process control, System Informer's deep inspection, WizTree's disk analysis — unified under a modern UI with Apple-quality polish and full cross-platform parity.

---

## Documentation Index

| Document | Purpose |
|----------|---------|
| [[feature-inventory]] | Every implemented feature by category with version introduced |
| [[gap-analysis]] | Process Lasso + WizTree feature comparison and priorities |
| [[release-history]] | Phase-to-release narrative, key decisions, session log links |
| [[architecture]] | Solution structure, key patterns, platform strategy |
| [[quick-reference]] | Build commands, paths, common tasks, gotchas |
| [[roadmap]] | Forward-looking plan derived from gap analysis |
| [[creation-history/README\|Creation History Archive]] | Phase 1–6 original design docs (historical) |

---

## Quick Links

- **Source:** `Areas/Projects/NexusSystemMonitor/`
- **CHANGELOG:** [[../CHANGELOG|CHANGELOG]]
- **Session logs:** `CC-Session-Logs/` (12 sessions, Phases 7–16)
- **GitHub:** `github.com/brass458/nexus-system-monitor`

---

## Current Status

| Area | Status |
|------|--------|
| Phases 1–16 | ✅ Complete |
| Windows support | ✅ Full (P/Invoke, PDH, WMI) |
| macOS support | ✅ Full (sysctl, Mach, ObjC runtime) |
| Linux support | ✅ Full (procfs, sysfs, multi-init) |
| CI/CD releases | ✅ 12 artifacts across 6 RIDs |
| Latest release | v0.1.6 — Dynamic surface swatch palettes |
| Next focus | See [[roadmap]] |
