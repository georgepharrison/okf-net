---
type: Reference
title: The Lint Severity Model
description: Four Roslyn-style severities, OKF-numbered diagnostics in reserved ranges, and defaults that block only spec conformance.
tags: [okf-net, lint, diagnostics, severity, configuration]
generated: { by: claude-fable/5, at: 2026-08-15T07:00:00Z }
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
| `OKF0102` | unused-source-id | A `sources[].id` is never cited by a footnote *reference* | warning |
| `OKF0103` | source-drift | A source's `last_modified` is later than `generated.at` | warning |
| `OKF0201` | self-verification | A `verified[].by` equals `generated.by` | warning |
| `OKF0202` | stale-concept | `stale_after` has passed | warning |
| `OKF0301` | missing-description | The concept has no `description` | warning |
| `OKF0302` | broken-internal-link | A bundle-internal link resolves to nothing | info |
| `OKF0303` | near-duplicate-concept | Title or filename collision after normalisation | warning |
| `OKF0304` | missing-tags | The concept has no `tags` | hidden |
| `OKF0305` | unregistered-tag | A tag is absent from `lint.tagRegistry` | hidden |
| `OKF0306` | generated-index-drift | A generated index differs from what `okf index` would emit | warning |
| `OKF0307` | missing-source-resource | A `sources[]` entry carries no `resource` | warning |
| `OKF0308` | unresolvable-source-resource | A `sources[].resource` written as a path names nothing in the bundle | info |
| `OKF0309` | link-leaves-bundle | A link resolves outside the bundle root | info |
| `OKF0310` | raw-item-mutated | An ingested `raw/` item no longer matches its recorded `sha256` | warning |

Two rules default to **hidden** rather than off: hidden is a severity, so
enabling a tag policy is a configuration edit rather than a feature flag.

`OKF0310` is the one rule scoped to the **vault** rather than to a bundle
root, because `raw/` sits outside every bundle root by construction. It follows
the vault a command resolved — the same vault whose `okf.json` decides these
severities — and a bundle handed over by path with no vault around it leaves the
rule inapplicable rather than failing. Detection is the manifest's recorded
`sha256` rather than git: the hash is the format-level record, it works in a
vault that is not a work tree, and it needs no process launched from a library
that is offline by contract.

A run says how many of those rules were live, because a clean report and a
silenced one are otherwise the same sentence:

```text
Checked 26 files in 1 bundle (19 rules: 19 active, 0 hidden): 0 errors, 0 warnings, 0 infos.
```

`--verbose` expands that to one line per rule — effective severity and the
configuration layer that set it — on standard error. The `--json` output stays a
bare array of diagnostic records, with no run-metadata envelope, because that
array is the contract the MCP server and CI annotations read; the same counts go
to standard error instead.

# Configuring it

Severity is set per rule in `okf.json` and overridable per invocation with
`--severity OKF####=level`; `treatAllWarningsAsErrors` promotes everything
currently at warning, which pointedly does not include broken internal links
because those default to info. An **unknown rule id in configuration is
itself reported** — a typo that silently disabled a rule would be the worst
possible failure mode for a linter.

This vault's own `okf/okf.json` promotes exactly four rules to error:
`OKF0306`, because nothing here is hand-edited that `okf index` owns;
`OKF0301`, because a concept with no description renders as a bare link in
its index and a snippet-less hit in search; `OKF0307`, because provenance
is the load-bearing half of the trust model and a source entry with no
`resource` records something nobody can go and check; and `OKF0310`, because
an ingested artifact that changed is a concept citing something that is no
longer there.

Those four are the promotions `okf init` writes into every vault it
scaffolds, with the reasons beside them in the file — see [vaults, registry,
and config](vault-registry-and-config.md) for why a config named `.json` is
parsed as JSONC precisely so those reasons can be there.

It also lifts the two tag rules off their hidden defaults to **warning**,
because the bundle now has a registry for them to check against. Warning
rather than error is a dated choice with a stated trigger for revisiting it;
the reasoning lives in [tagging
discipline](../practices/tagging-discipline.md), where a rule that governs
the vocabulary belongs, rather than here, where the rules are catalogued.

# Deliberate limits

- **Broken links are info, never an error by default.** The specification
  requires consumers to tolerate them: a dangling link is often knowledge not
  yet written. A link that resolves *outside* the bundle root is reported
  separately, also at info: it is spec-tolerated, but leaving it silent made it
  indistinguishable from a correct link, and a bundle whose links only work from
  inside one checkout is not portable.
- **A `sources[].resource` is only checked when it unambiguously reads as a
  path.** The specification lets the field be a scope descriptor (`all queries in
  BigQuery project X`) or an absolute URL, and no rule here ever touches the
  network. What is left — a path that names nothing in the bundle — is reported at
  info, and is resolved from the citing concept first and the bundle root second,
  because producers write both.
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
