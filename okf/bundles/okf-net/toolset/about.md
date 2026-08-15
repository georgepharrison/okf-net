---
type: Guide
title: About the Toolset Domain
description: How okf-net is built — layering, lint severities, search semantics, index generation, and vault resolution.
tags: [okf-net, architecture, domain]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
---

Concepts in this directory describe **okf-net itself** — the choices an
implementation of [the format](../format/about.md) had to make, and the
reasoning that survived contact with the code.

They are curated knowledge, not a specification: `docs/decisions.md` and
`docs/prd.md` in this repository stay authoritative, and each concept here
cites them.[^decisions]

- [Library, CLI, MCP layering](library-cli-mcp-layering.md) — why the library
  is the core and both adapters are thin.
- [The lint severity model](lint-severity-model.md) — four severities,
  numbered diagnostics, and a default line drawn exactly at the spec.
- [Search semantics](search-semantics.md) — deterministic BM25 over
  field-weighted concept text.
- [Index generation and drift](index-generation-and-drift.md) — generated
  files, the marker that makes them claimable, and the five drift states.
- [Vaults, registry, and config](vault-registry-and-config.md) — where
  bundles are found and which layer wins.

[^decisions]: okf-net — Architecture Decisions, "Repo layout".
