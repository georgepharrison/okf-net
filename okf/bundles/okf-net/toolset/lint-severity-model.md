---
type: Reference
title: The Lint Severity Model
description: Four Roslyn-style severities, OKF-numbered diagnostics in reserved ranges, and defaults that block only spec conformance.
tags: [okf-net, lint, diagnostics, severity, configuration]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: prd
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/prd.md
    title: okf-net — Product Requirements
    author: "human:ringo"
    last_modified: 2026-08-14
---

`okf lint` borrows Roslyn's shape wholesale: four severities — **hidden**,
**info**, **warning**, **error** — numbered diagnostic identifiers, and
per-rule configuration with no exemptions.[^decisions]

One principle governs the defaults:

> **Defaults block only what the specification says. Every additional block
> is consumer configuration.**

So the only out-of-the-box errors are the three §11 conformance rules. A
bundle that is conformant but trips every other rule in the set still exits
zero. A bundle that violates §11 exits non-zero no matter what the
configuration says. This is the same commitment as [foreign-bundle
tolerance](../format/foreign-bundle-tolerance.md), viewed from the
configuration side.

# Identifier ranges

Identifiers are `OKF####`, with reserved category ranges so a number is
readable before it is looked up.[^prd] Nicknames are documentation only and
are never valid configuration keys — configuration references the number.

| Range | Category |
| --- | --- |
| `00xx` | conformance |
| `01xx` | provenance |
| `02xx` | trust |
| `03xx` | hygiene |

# The shipped rules

| Id | Nickname | Fires when | Default |
| --- | --- | --- | --- |
| `OKF0001` | unparseable-frontmatter | No frontmatter block, or one that does not parse | error |
| `OKF0002` | missing-type | No non-empty `type` | error |
| `OKF0003` | invalid-index-structure | An `index.md` breaks §8 | error |
| `OKF0004` | invalid-log-structure | A `log.md` breaks §9 | error |
| `OKF0101` | uncited-footnote | A footnote label joins to no `sources[].id` | warning |
| `OKF0102` | unused-source-id | A `sources[].id` is never cited | warning |
| `OKF0103` | source-drift | A source's `last_modified` is later than `generated.at` | warning |
| `OKF0201` | self-verification | A `verified[].by` equals `generated.by` | warning |
| `OKF0202` | stale-concept | `stale_after` has passed | warning |
| `OKF0301` | missing-description | The concept has no `description` | warning |
| `OKF0302` | broken-internal-link | A bundle-internal link resolves to nothing | info |
| `OKF0303` | near-duplicate-concept | Title or filename collision after normalisation | warning |
| `OKF0304` | missing-tags | The concept has no `tags` | hidden |
| `OKF0305` | unregistered-tag | A tag is absent from `lint.tagRegistry` | hidden |
| `OKF0306` | generated-index-drift | A generated index differs from what `okf index` would emit | warning |

Two rules default to **hidden** rather than off: hidden is a severity, so
enabling a tag policy is a configuration edit rather than a feature flag.

# Configuring it

Severity is set per rule in `okf.json` and overridable per invocation with
`--severity OKF####=level`; `treatAllWarningsAsErrors` promotes everything
currently at warning, which pointedly does not include broken internal links
because those default to info. An **unknown rule id in configuration is
itself reported** — a typo that silently disabled a rule would be the worst
possible failure mode for a linter.

This vault's own `okf/okf.json` promotes exactly two rules to error:
`OKF0306`, because nothing here is hand-edited that `okf index` owns, and
`OKF0301`, because a concept with no description renders as a bare link in
its index and a snippet-less hit in search.

# Deliberate limits

- **Broken links are info, never an error by default.** The specification
  requires consumers to tolerate them: a dangling link is often knowledge not
  yet written. A link that resolves *outside* the bundle root is not
  bundle-internal and is not reported at all.
- **Staleness never blocks by default.** An expired date is a prompt, not a
  defect.
- **Near-duplicate detection is a cheap heuristic for now** — title or
  filename collision after lowercasing and dropping non-alphanumerics,
  compared within a bundle. Files whose name is itself a convention
  (`about.md`) are exempt from both arms, because a conventional file's
  identity is its directory, not its name, and `title: About` repeated across
  directories is as much a convention as the filename is. The vectorisation
  spike will replace the heuristic; the rule id will not change when it does.
- **Reserved-file structure checks stay light** for the reason given in
  foreign-bundle tolerance.

[^decisions]: okf-net — Architecture Decisions, §7, Q2, Q5, Q8, and the `okf lint` milestone.
[^prd]: okf-net — Product Requirements, CLI-5, CLI-6, CLI-7, CLI-15.
