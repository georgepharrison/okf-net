---
type: Guide
title: About the Format Domain
description: OKF v0.2 itself — bundle shape, provenance, trust, and consumer obligations.
tags: [okf, format, spec, domain]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/3fcbb9f828c2f23d109c855ee403c3a4c81f3a96/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
---

Concepts in this directory describe **the format**, not the toolset. They
would still be true if okf-net were rewritten in another language or replaced
outright, which is why they live apart from [the toolset
domain](../toolset/about.md).

The specification is the authority for everything here.[^okf-spec] These
concepts exist to record the parts okf-net had to take a position on, the
consequences that are easy to rediscover the hard way, and the reading the
implementation actually settled on where the spec left room.

- [OKF v0.2, the format](okf-format-v0-2.md) — what a bundle is and what
  conformance actually requires.
- [Bundle self-description](bundle-self-description.md) — the layout
  conventions okf-net puts on top, and the README trap that forced them.
- [Provenance: capture versus cite](provenance-capture-vs-cite.md) — the
  one-question test for how a source is recorded.
- [Trust tiers and acknowledgment](trust-tiers-and-acknowledgment.md) — how
  confidence in a concept is derived rather than stored.
- [Foreign-bundle tolerance](foreign-bundle-tolerance.md) — the constraint
  that keeps okf-net an OKF tool rather than an okf-net tool.

[^okf-spec]: Open Knowledge Format (OKF), version 0.2.
