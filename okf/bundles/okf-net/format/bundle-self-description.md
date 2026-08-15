---
type: Concept
title: Bundle Self-Description Conventions
description: The layout conventions okf-net layers on OKF, and the README trap that forced bundle root apart from repo root.
tags: [okf, format, conventions, layout, readme-trap]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
---

OKF requires no specific files.[^okf-spec] A bundle that is nothing but
conformant concepts is a valid bundle. Everything below is convention that
okf-net adds for the bundles *it* produces — never a requirement it imposes
on bundles it merely reads.

# The README trap

`README.md` is not a reserved filename. Inside a bundle root it is therefore
an ordinary concept, and an ordinary concept without frontmatter is a
conformance error. A repository that makes its own root a bundle root breaks
the moment someone adds the file every repository has.

The rule that falls out is blunt and worth stating as a rule rather than as
an anecdote: **a bundle root is never a repository root.**[^decisions] Hence
the project layout, in which repo-facing documentation sits one level above
every bundle:

```text
<project>/okf/
  README.md          # repo-facing, safely outside every bundle root
  okf.json           # project config — the committed team contract
  bundles/<name>/    # one or many bundle roots
  custodian/         # skill, prompts, recipe config, hook scripts (strippable)
  raw/               # drop zone for captured artifacts, outside every bundle
```

The same reasoning places `raw/` outside the bundles: a `.md` file dropped
into a drop zone inside a bundle root would be a frontmatter-less concept and
would break conformance on arrival. See [provenance: capture versus
cite](provenance-capture-vs-cite.md) for what `raw/` is for, and [vaults,
registry, and config](../toolset/vault-registry-and-config.md) for how the
layout is discovered.

# Entry-point convention

For bundles okf-net produces:

- The bundle-root `index.md` is always generated and carries
  `okf_version: "0.2"` frontmatter — the one place the specification permits
  frontmatter on an index.
- The bundle carries an `about-this-bundle.md` concept naming the toolset,
  the custodian, the update cadence, and the consumption options.
- Each subdirectory carries an `about.md` whose `description` becomes that
  directory's blurb in the parent index. This is the whole reason the
  convention exists: it is what lets index generation stay deterministic and
  offline instead of asking a model to summarise a directory.

For foreign bundles with no index, okf-net synthesises one in memory rather
than writing into a tree it does not own — a move the specification
explicitly sanctions.

# Declarative pointers, not prose

Tooling a bundle depends on is wired through frontmatter — `executor.resource`
and `attester.resource` for attested computations — and never mentioned only
in prose. A pointer in frontmatter can be resolved, validated, and rewritten
by a machine; a tool name buried in a paragraph can only be grepped for and
hoped about.

The corollary is that a bundle *describes* its tooling and never ships it.
Consumption stays read-only: reading a bundle must never require executing
anything it contains, which is also why okf-net records executors and
attesters but never runs them.

[^okf-spec]: Open Knowledge Format (OKF), version 0.2, §§3.1, 8, 11.
[^decisions]: okf-net — Architecture Decisions, §2 and Q3.
