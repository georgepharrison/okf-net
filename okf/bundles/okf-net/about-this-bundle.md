---
type: Guide
title: About This Bundle
description: What the okf-net bundle covers, who maintains it, and how to consume it.
tags: [okf-net, bundle, meta, dogfood]
generated: { by: claude-fable/5, at: 2026-08-14T21:42:00-05:00 }
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
that also hosts an MCP server (`okf mcp`), and a pair of agent skills. Of
those, `Okf.Core`, the CLI's `lint`, `index`, and `search` commands, and the
MCP server are built today; the skills are a later milestone.

The bundle is the toolset's own dogfood: okf-net's knowledge, kept in
okf-net's format, validated by okf-net in okf-net's pipeline. It doubles as
the project's reference example of documenting a code repository with OKF.

# What is in here

Knowledge is organised **domain-first**: the directory tree follows the
subject matter, the kind of document lives in frontmatter `type`, and
anything cross-cutting is a `tag`.[^decisions] The specification itself
leaves layout to the producer — domain-first is okf-net's choice on top of
it, taken in preference to an epistemic-type folder layout.

- [The format](format/about.md) — OKF v0.2 itself: the shape of a bundle,
  provenance, trust, and what a consumer is obliged to tolerate. This
  knowledge outlives okf-net.
- [The toolset](toolset/about.md) — how okf-net is built: layering, the lint
  severity model, search semantics, index generation, and vault resolution.
- [Practices](practices/about.md) — how the project is run: the custodian
  model, the dependency license gate, and the release scheme.

# What is not in here

`docs/decisions.md` and `docs/prd.md` in this repository remain the
authoritative development documents. Every concept here cites them; none
replaces them. Rationale for a decision belongs in the decision log, and
looking for it in two places is how the two drift apart.

This repository never holds a personal knowledge vault. That lives at
`~/okf/` and is reached through the registry, never from here.

# Custodial status

**No custodian is active on this bundle yet.** Every concept was written by
hand in a single pass, stamped `generated.by: claude-fable/5`, and left
unverified — no `verified` events at all, so the whole bundle sits at the
lowest trust tier by construction. That is accuracy rather than modesty:
nothing here has been independently confirmed against its sources.

The custodian skill that will maintain it does not exist yet, and the
`custodian/` and `raw/` directories the project layout reserves are
deliberately absent until it does. Activation — a custodian, a tag registry,
human verification stamps, and a `log.md` — is tracked as *Dogfood v2*.

Until then, updates happen the way any other change to this repository does:
someone edits a concept, runs `okf index`, and the pipeline's `dogfood` job
either agrees or fails the build.

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
- **MCP.** `okf mcp [path]` runs a read-only server over stdio from the same
  binary, exposing `okf_list`, `okf_search`, and `okf_read`. It resolves
  vaults and scope by exactly the CLI's rules, so the two never disagree
  about what they are looking at — `okf_search` returns the same JSON array
  `okf search --json` prints, from the same writer.

[^prd]: okf-net — Product Requirements, §1.
[^decisions]: okf-net — Architecture Decisions, "Vision".
