---
type: Concept
title: The Custodian Model
description: A custodian maintains a bundle from beside it, never inside it, and surfaces machine-derived insight for review rather than landing it silently.
tags: [okf-net, custodian, agents, maintenance, ci]
generated: { by: claude-fable/5, at: 2026-08-14T23:23:59-05:00 }
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

A **custodian** is the agent or process that maintains a bundle: discovery,
enrichment, prose writing, index regeneration, and the log. It is
producer-side machinery, triggered by git hooks and CI. A **bundler** is its
counterpart on the way out, packaging a bundle for distribution by stripping
the custodian machinery so a consumer receives only readable
markdown.[^decisions]

Consumers are **consume-only**. A bundle is readable markdown, and reading it
never requires executing anything — which is the single constraint that makes
everything else about the custodian a producer's private business.

# Where the machinery lives

Beside the bundle, never inside it: `<project>/okf/custodian/` holds the
skill, the prompts, the recipe configuration, and the hook scripts, and the
bundler strips the whole directory on the way out.

The shared *toolset* — this repository — is referenced by version and never
vendored per project. Vendoring a copy of the tooling into every project is
how eight projects end up on six versions of the same linter with nobody able
to say which behaviour is current.

In-bundle scripts, where they exist at all, are Python: they must run in
arbitrary consumer and CI environments, and Python is the one interpreter
that is reliably already there. **Prose instructions and small deterministic
scripts may live in a bundle; intelligence lives outside it.**

# Prose versus structured

The division of labour that the whole model rests on:

- **Frontmatter** is the few machine-queryable fields. Code operates here.
- **The markdown body** is prose for humans and language models. Agents
  operate here.

A custodian is therefore not a code generator with better manners. It writes
the layer that code cannot check and leaves the layer code *can* check to the
tools — which is why `okf index` regenerates indexes rather than the
custodian hand-editing them, and why [drift is a lint
rule](../toolset/index-generation-and-drift.md) rather than a matter of
custodial discipline.

# Surfacing, not landing

Machine-derived insight is **offered for review, never landed silently**. The
chain is deliberately built out of primitives the format and the host forge
already provide — no new frontmatter field, no bespoke review queue:

1. The custodian writes or updates a concept and stamps
   `generated.{by,at}` — and nothing else. It never writes `verified` for its
   own output, because [an actor cannot verify
   itself](../format/trust-tiers-and-acknowledgment.md).
2. Uncertain output carries `status: draft`, which makes the concept
   unacknowledged by definition.
3. `okf inbox` lists everything unacknowledged; `okf verify` clears an item
   with a human stamp.
4. A custodian running in CI opens a merge request. **The merge request is
   the inbox** for machine-derived insight — reviewable, and, importantly,
   ignorable.
5. `log.md` records notable updates, newest first.

The staleness-refresh loop is the same chain with a different trigger: when a
version-pinned source's `stale_after` expires, the custodian fetches release
notes from the pinned version to current, updates or drafts the affected
concept with a derived impact analysis, and surfaces it exactly as above.

That loop is post-MVP, and so is most of the chain it rides on: of the five
steps, only the staleness trigger is implemented today, reported by
`okf lint` as `OKF0202`. `okf inbox` and `okf verify` are designed and not
yet built. The chain is the contract the custodian is being built to, not a
description of what runs on this repository now.

# Search before create

The capture skill searches the resolved bundles *before* writing anything, and
either extends the concept it found or states why a new one is warranted. A
knowledge base that cannot resist adding a fourth document about the same
thing decays into a search problem, and then into an abandoned directory.

# The two skills

The custodian is instantiated by two prose skills shipped in the toolset's
`skills/` directory, host-neutral and calling the CLI rather than
reimplementing anything:[^prd]

- **`okf-capture`** turns something just learned into a concept: search first,
  apply [the capture-versus-cite
  test](../format/provenance-capture-vs-cite.md), drop what cannot defend
  itself into `raw/` with an entry in the capture manifest
  (`okf/raw/manifest.json`), and stamp `generated`.
- **`okf-custodian`** maintains the bundle: ingest a `raw/` item into an
  ordinary `references/` concept, enrich prose, regenerate indexes, clear
  lint, and append to `log.md`.

Both state the same doctrine — orient by disclosure, retrieve by search, open
what you pick; `raw/` is evidence and the bundle is knowledge — which is also
what `okf mcp` tells a client in its tool descriptions.

# Status in this bundle

The skills exist; no custodian is active on *this* bundle. `custodian/` and
`raw/` are deliberately absent from this vault, every concept here is
`unverified`, and the concepts are maintained by hand under review. Activation
is tracked as *Dogfood v2*.[^prd]

[^decisions]: okf-net — Architecture Decisions, §1, §3, §7.
[^prd]: okf-net — Product Requirements, §2.4 and ACC-5.
