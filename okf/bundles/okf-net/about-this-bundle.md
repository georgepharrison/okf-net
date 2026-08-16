---
type: Guide
title: About This Bundle
description: What the okf-net bundle covers, who maintains it, and how to consume it.
tags: [okf-net, bundle, meta, dogfood]
generated: { by: claude-fable/5, at: 2026-08-16T09:00:00Z }
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

This is the knowledge bundle for **okf-net**, a .NET toolset for producing,
validating, and consuming OKF v0.2 knowledge bundles.[^prd] It is designed to
ship as a library (`Okf.Core`), a single self-contained CLI binary (`okf`)
that also hosts an MCP server (`okf mcp`), and a set of agent skills. All
three are built: `Okf.Core`, every CLI verb — `init`, `lint`, `index`,
`search`, `register`, `unregister`, `registry`, `inbox`, `verify`, `bundle`,
`site`, `mcp`, plus `help` and `version` — the MCP server, and the three
skills in `skills/`: two that produce knowledge and one that consumes it. What
remains designed rather than built is the Pi shim.

The bundle is the toolset's own dogfood: okf-net's knowledge, kept in
okf-net's format, validated by okf-net in okf-net's pipeline. It doubles as
the project's reference example of documenting a code repository with OKF.

# What is in here

Knowledge is organised **domain-first**: the directory tree follows the
subject matter, the kind of document lives in frontmatter `type`, and
anything cross-cutting is a `tag`, drawn from [a closed
registry](practices/tagging-discipline.md).[^decisions] The specification itself
leaves layout to the producer — domain-first is okf-net's choice on top of
it, taken in preference to an epistemic-type folder layout.

- [The format](format/about.md) — OKF v0.2 itself: the shape of a bundle,
  provenance, trust, and what a consumer is obliged to tolerate. This
  knowledge outlives okf-net.
- [The toolset](toolset/about.md) — how okf-net is built: layering, the lint
  severity model, search semantics, index generation, and vault resolution.
- [Practices](practices/about.md) — how the project is run: the custodian
  model, tagging discipline, the dependency license gate, and the release
  scheme.
- [References](references/about.md) — external material captured into the
  vault and rendered here, because its source could not be pinned and cited
  in place.

# What is not in here

`docs/decisions.md` and `docs/prd.md` in this repository remain the
authoritative development documents. Every concept here cites them; none
replaces them. Rationale for a decision belongs in the decision log, and
looking for it in two places is how the two drift apart.

This repository never holds a personal knowledge vault. That lives at
`~/okf/` and is reached through the registry, never from here.

# Custodial status

**A custodian is active on this bundle.** `okf/custodian/` names the two
skills that maintain it, `okf/raw/` holds what has been captured, and the
pipeline's `dogfood` job runs `okf lint`, `okf index --check`, and the capture
manifest's invariant check on every push. [The custodian
model](practices/custodian-model.md) states exactly what runs where.

Two things that does *not* mean. Nothing refreshes itself: the scheduled
`custodian-inbox` job runs `okf inbox` and publishes what it finds, and every
step after that — fetching what changed, drafting it, landing it — is a
person's. And every concept here remains **unverified** — no `verified` events
at all, so the whole bundle sits at the lowest trust tier by construction and
every concept in it is on the inbox. That is accuracy rather than modesty:
nothing here has been independently confirmed against its sources by a second
actor.

Updates therefore still begin with a person: someone opens a session, loads a
skill, writes, runs `okf index`, and the `dogfood` job either agrees or fails
the build. `log.md` records the notable ones.

# How to consume it

The bundle is plain markdown, so the floor is `git clone` and a text editor:
start at `index.md` and follow the links. Nothing here requires executing
anything.

Beyond that:

- **Progressive disclosure.** Every directory carries a generated `index.md`
  and every subdirectory an `about.md` describing it, so an agent can walk
  the tree top-down and read only the branch it needs.
- **The CLI.** `okf search <query>` returns ranked, links-first results with
  a trust tier and stale flag on each hit; `okf lint` reports on the bundle
  without modifying it.
- **An agent, taught.** `skills/okf-vault/SKILL.md` is the consumer skill:
  it walks an agent from a question to the concepts that answer it, over the
  CLI or the MCP tools, and back to a citation carrying the trust tier. Its
  producer siblings are `okf-capture` and `okf-custodian`; `skills/README.md`
  says which fires when.
- **As a distributed archive.** `okf bundle` packages this bundle for
  consume-only distribution, and every release ships the result as
  `okf-net-knowledge.tar.gz`. What you receive is `bundles/okf-net/**` plus an
  `okf-bundle.json` recording a `sha256` per file — never this repository's
  `raw/` captures or its custodian machinery. See [bundling and
  distribution](toolset/bundling-and-distribution.md).
- **As a browsable site.** `okf site` renders this vault as a self-contained
  static site — a landing page, a trust dashboard whose tiles and tags are
  filters, a cross-link graph coloured by trust tier, and one page per file.
  The repository's `pages` job publishes it from the default branch, and
  `--single-file` collapses the whole thing into one HTML file you can hand
  to someone. No third-party JavaScript, no network request, so it works the
  same from a `file://` path.
- **MCP.** `okf mcp [path]` runs a read-only server over stdio from the same
  binary, exposing `okf_list`, `okf_search`, and `okf_read`. It resolves
  vaults and scope by exactly the CLI's rules, so the two never disagree
  about what they are looking at — `okf_search` returns the same JSON array
  `okf search --json` prints, from the same writer.

[^prd]: okf-net — Product Requirements, §1.
[^decisions]: okf-net — Architecture Decisions, "Vision".
