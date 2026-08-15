---
type: Concept
title: Foreign-Bundle Tolerance
description: okf-net must handle bundles it did not produce with no in-bundle cooperation, which is what keeps it an OKF tool.
tags: [okf, compatibility, conformance, acceptance, interop]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: prd
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/prd.md
    title: okf-net — Product Requirements
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
---

A **foreign bundle** is one okf-net did not produce. Pointing the tool at
one must yield lint, index synthesis, and search with no cooperation from the
bundle whatsoever — no marker file, no configuration, no conversion step.

The acceptance test is concrete: `okf lint` reports Google's four published
bundles — `acme_retail`, `ga4`, `stackoverflow`, `crypto_bitcoin` — as
conformant, exiting zero under default severity, checked as-is from
upstream.[^prd] Warnings on them are expected and fine. **Any error on those
bundles is a defect in okf-net, not in the bundles.**

That single sentence is doing most of the work in this concept. It converts
"be tolerant" from a virtue into a test that fails.

# What the reference bundles exercise

They are not a soft target. Between them they carry `viz.html` and `.py`
files sitting in the tree, `references/` directories holding first-class
concepts, attested-computation concepts with `executor` and `attester`
wiring, a `not:` producer extension that appears nowhere in the
specification, a bare-mapping `verified` with no list dash, hand-styled
indexes with bold titles and prose bullets, and root indexes with no
`okf_version` at all.

Every one of those is something a naive implementation would reject.

# What the constraint forces

- **Defaults block only what the specification says.** The three conformance
  rules are the only errors out of the box; everything else okf-net can say
  is a warning or quieter, and promoting any of it is the consumer's choice
  about their own vault. See [the lint severity
  model](../toolset/lint-severity-model.md).
- **Generated files are marked, and only marked files may drift.** A
  hand-written index is a foreign artifact okf-net reports on but never
  blames — and never silently overwrites. See [index generation and
  drift](../toolset/index-generation-and-drift.md).
- **Deliberate divergences are enumerated, not incidental.** Where okf-net's
  index generator differs from the reference implementation's — ordering by
  filename, pinning the subdirectory section last, taking subdirectory blurbs
  from `about.md` rather than from a model — each difference is written down
  and tested. A divergence nobody wrote down is a bug waiting to be
  discovered by someone else.[^decisions]
- **Reserved-file checks stay light.** `index.md` and `log.md` are validated
  for the structure §8 and §9 actually state and no further, because anything
  stricter starts erroring on bundles that were fine before okf-net existed.
- **Synthesis never writes.** A bundle with no index gets one built in
  memory. Writing into a tree the tool does not own would be a repair nobody
  asked for.

# Why this is load-bearing

The format is the interop layer, not the tool. Nothing okf-net produces may
require okf-net to consume, and nothing okf-net consumes may require okf-net
to have produced it. A toolset that only handles its own output has quietly
forked the format and called it compatibility.

[^prd]: okf-net — Product Requirements, ACC-1 and CORE-3.
[^decisions]: okf-net — Architecture Decisions, the `okf index` milestone.
