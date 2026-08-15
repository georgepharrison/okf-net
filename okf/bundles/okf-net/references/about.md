---
type: Guide
title: About the References Domain
description: External material captured into the vault and rendered here as ordinary concepts, so a claim survives its source.
tags: [okf-net, references, provenance, capture, domain]
generated: { by: claude-fable/5, at: 2026-08-14T23:54:00-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
---

Concepts in this directory are **renderings of material from outside the
project**. Each one exists because its source failed [the capture-versus-cite
test](../format/provenance-capture-vs-cite.md): a living document with no
version to pin, which a concept could not be re-verified against once it
changed or vanished.

The directory name carries no meaning of okf-net's own. It is the plain
spec §6.3 convention for mirroring external material as first-class concepts,
so a foreign bundle using the same name is never reinterpreted, and nothing in
here is read-only or otherwise special.[^decisions]

The original artifact is not here. It sits in `okf/raw/`, outside every bundle
root, recorded in the capture manifest by a dated id that each concept below
names in its prose — so a distributed copy of this bundle still says exactly
what was retrieved and when, even though the archive stayed producer-side.

Analysis belongs in the concept that *cites* one of these, not in the rendering
itself.

- [Equipping agents for the real world with Agent Skills](agent-skills.md) —
  what a skill is, and progressive disclosure as its core design principle.

[^decisions]: okf-net — Architecture Decisions, Q3 resolution and the capture-and-custodian-skills milestone.
