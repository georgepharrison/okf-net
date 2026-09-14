---
type: Guide
title: About the Toolset Domain
description: How okf-net is built — layering, lint severities, search semantics, index generation, vault resolution, distribution, and running the tool on its own knowledge.
tags: [okf-net, architecture, domain]
generated: { by: "pi/qwen3.8-flash-next", at: 2026-09-14T08:07:20Z }
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

- [The architecture spine](architecture-spine.md) — what `docs/architecture.md`
  is for, the shape of an `AD` rule, and the conventions that keep the
  document safe to regenerate.
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
- [Bundling and distribution](bundling-and-distribution.md) — what a consumer
  receives, why the archive is byte-reproducible, and what becomes of a link
  that leaves the bundle.
- [A capability describes itself](capability-describes-itself.md) — why the
  bundle carries a concept for a command that shipped before it, what the
  command's own output proves about it, and what it leaves for a person.

[^decisions]: okf-net — Architecture Decisions, "Repo layout".
