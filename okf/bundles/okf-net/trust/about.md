---
type: Guide
title: About the Trust Domain
description: How okf-net decides whether anybody has ever stood behind a concept, and what it does when it cannot tell
tags: [okf-net, trust, verification, lifecycle, provenance, domain]
generated: { by: openai-codex/gpt-5.6-luna, at: 2026-09-13T00:00:00Z }
sources:
  - id: issue-71
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/71
    title: "Add `okf candidates`: enumerate the concepts with zero verification history"
    author: "human:ringo"
    last_modified: 2026-09-13
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/dev/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-09-13
---

This directory holds what okf-net knows about **verification as a fact rather than a
feeling**: whether a `verified` block records anybody, what the tool does when the block
claims verification it cannot read, and which surface answers which question.

It is deliberately separate from [the format domain](../format/about.md), where
[trust tiers and the acknowledgment model](../format/trust-tiers-and-acknowledgment.md)
lives.[^decisions] That concept describes the spec's own vocabulary — the tiers §5.3 derives and the
acknowledgment rule §5.2 implies. The concepts here describe the *judgement* okf-net makes
on top of that vocabulary, and the one place the two readings of the same bytes are allowed
to disagree.

The load-bearing distinction in every file here is that **"nobody signed this" and "I cannot
tell whether anybody signed this" are different answers**, and collapsing them is how
malformed metadata hides a concept from review. A tool that quietly omits a concept is worse
than one that admits it could not read the file — which is why the surfaces in this domain
report a gap instead of printing a shorter list.[^issue-71]

- [Candidate enumeration and the completeness contract](candidates-and-completeness.md) —
  the eligibility question, why the shared normalizer could not answer it, and the
  exit-code contract that makes an incomplete inventory visible.

[^issue-71]: Add `okf candidates`: enumerate the concepts with zero verification history.

[^decisions]: okf-net — Architecture Decisions, the candidate-enumeration entry.
