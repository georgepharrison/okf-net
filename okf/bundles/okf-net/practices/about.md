---
type: Guide
title: About the Practices Domain
description: How the okf-net project is run — custodianship, tagging discipline, the dependency license gate, and the release scheme.
tags: [okf-net, practices, process, domain]
generated: { by: claude-fable/5, at: 2026-08-15T00:12:00-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
---

Concepts in this directory describe **how the project is run** rather than
what it builds: the policies a contributor or an agent has to satisfy, and
the reasoning behind each.[^decisions]

They sit apart from [the toolset domain](../toolset/about.md) because they
would survive a rewrite of the code and would not survive a change of
governance — which is the reverse of everything in there.

- [The custodian model](custodian-model.md) — the agent that maintains a
  bundle, where its machinery lives, and why it never ships inside the
  bundle.
- [Tagging discipline](tagging-discipline.md) — the bundle's tag vocabulary,
  what each facet is for, and how a new tag gets added.
- [The dependency license gate](dependency-license-gate.md) — the
  Apache-2.0-compatibility rule and the CI job that enforces it.
- [Release and versioning](release-and-versioning.md) — conventional
  commits, semantic-release, and the pre-1.0 prerelease channel.

[^decisions]: okf-net — Architecture Decisions, §1 and the MS-PL ruling.
