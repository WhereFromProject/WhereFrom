# AGENTS.md

## Project

This repository contains **WhereFrom**, a Windows-first, local-first file provenance tool.

The long-term goal is to help users understand where files came from and, in later versions, how files may have moved or been derived.

Before making architectural changes, read:

- `ENGINEERING.md`

`ENGINEERING.md` describes the product direction, architecture, data model, milestones, and long-term roadmap.

This file defines how coding agents should work inside the repository.

---

## Current Development Scope

Unless the user explicitly says otherwise, work only on the currently assigned milestone.

Do not proactively implement later roadmap items.

For v0.1, do not add any of the following unless the current task explicitly requires them:

- GUI
- SQLite
- browser extension
- background service
- filesystem watcher
- file move/rename tracking
- file lineage graph
- cloud sync
- telemetry
- user accounts
- AI features
- OCR
- automatic classification
- package update functionality

Do not expand the scope merely because a future feature appears in `ENGINEERING.md`.

The user's current task and milestone take precedence over roadmap sections.

---

## Engineering Principles

### 1. Keep the implementation simple

Prefer the smallest design that fully satisfies the current milestone.

Do not introduce abstractions, frameworks, plugins, dependency injection infrastructure, event buses, caches, or extensibility layers only because they might be useful later.

Avoid speculative architecture.

If a simpler implementation is sufficient, use it.

---

### 2. Preserve platform boundaries

`WhereFrom.Core` must remain platform-independent.

Windows-specific behavior belongs in Windows-specific projects, such as:

```text
WhereFrom.Platform.Windows