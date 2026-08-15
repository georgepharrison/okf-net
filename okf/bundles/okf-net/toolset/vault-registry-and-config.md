---
type: Concept
title: Vaults, Registry, and Configuration
description: How okf-net finds bundles, why the personal vault is just a registry entry, and which configuration layer wins.
tags: [okf-net, vault, registry, configuration, discovery]
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

A **vault** is a directory holding `bundles/`. Two exist by default: the
project vault, found by walking up from the working directory for a directory
named `okf/`, and the personal vault at `~/okf/` — visible, not hidden,
because knowledge someone accumulates for years should not live somewhere
`ls` refuses to mention.[^decisions]

Every command resolves its working set the same way, which is the property
that keeps `okf lint` and `okf search` from ever disagreeing about what they
are looking at:

1. an explicit path argument, which overrides everything and may name any
   bundle root including a foreign one, then
2. the project vault found by walk-up, then
3. the personal vault (`OKF_HOME`, else `~/okf/`).

Resolution is reported under `--verbose`, so "which bundle did it actually
read" is never a guess. Discovery itself is pure: it reads the filesystem and
configuration and writes nothing.[^prd]

# The registry

`~/.config/okf/` lists known bundles, and **the personal vault is an ordinary
entry in it** — no special-casing, no privileged path. Everything beyond the
current project enters through the registry, which means there is exactly one
mechanism to reason about rather than one mechanism plus an exception.

`okf register` and `okf unregister` are explicit and idempotent:
re-registering a known path and unregistering an unknown one are both
no-op successes. **Auto-registration is off by default**, with a global
setting to opt into it.

# Scope is project-only by default

Search sees the project's bundles and nothing else unless configuration or a
flag opts registry entries in. The reason is team determinism: a query must
return identical results on every developer's machine and in CI. A search
that silently widened to include whatever happened to be on one contributor's
laptop would produce answers nobody else could reproduce — and the same
reasoning is what makes [search
semantics](search-semantics.md) refuse a clock and a network call.

# Configuration layers

Precedence, highest first:

| Layer | Location |
| --- | --- |
| CLI arguments | the invocation |
| `OKF_HOME` | environment |
| Project config | `<project>/okf/okf.json` |
| Global config | `$XDG_CONFIG_HOME/okf/okf.json`, else `~/.config/okf/okf.json` |

A setting present at a higher layer wins; lower layers still supply unset
keys, and `--verbose` reports the effective value together with the layer it
came from.

The **project config is committed, and that is the point**: it is the team
contract, reviewed like code. It sits at the vault root rather than as a
repository dotfile for two reasons — a command that resolved the vault has
therefore already found the config, with no second search, and a file inside
`okf/` reads as a statement about this vault rather than as one more thing
cluttering the repository root.

`OKF_HOME` is deliberately **not** a severity layer. It moves the personal
vault — that is, it changes which bundles get linted — and never changes a
rule's severity. Environment variables that quietly rewrite policy are how a
build passes locally and fails in CI for reasons nobody can reconstruct.

[^decisions]: okf-net — Architecture Decisions, §6, Q4, and the `okf lint` milestone.
[^prd]: okf-net — Product Requirements, CLI-1 through CLI-4, CORE-13.
