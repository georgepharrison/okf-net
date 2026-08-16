---
type: Concept
title: Vaults, Registry, and Configuration
description: How okf-net finds bundles, why the personal vault is just a registry entry, and which configuration layer wins.
tags: [okf-net, vault, registry, configuration, discovery]
generated: { by: claude-fable/5, at: 2026-08-16T09:00:00Z }
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

A **vault** is a directory holding `bundles/`. `okf init` creates one:
`README.md` outside every bundle root, `okf.json`, `custodian/`, `raw/` with
its capture manifest, and one bundle carrying `about-this-bundle.md`, a
`log.md`, and a generated `index.md`. It writes only what is missing, so a
second run reports what is there and changes nothing, and it refuses to
initialize inside a bundle root — see [bundle self-description
conventions](../format/bundle-self-description.md) for the trap that refusal
avoids. Two exist by default: the
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

`$XDG_CONFIG_HOME/okf/registry.json` — else `~/.config/okf/registry.json` —
lists known vaults and bundles, and **the personal vault is an ordinary entry
in it**: no special-casing, no privileged path. Everything beyond the current
project enters through the registry, which means there is exactly one
mechanism to reason about rather than one mechanism plus an exception.

`okf register [path]` and `okf unregister [path|id]` are explicit and
idempotent: re-registering a known path and unregistering an unknown one are
both no-op successes that exit 0 and say so. `okf registry list` reports every
entry, marking any whose path has gone away, and `okf registry prune` removes
those. **Auto-registration is off by default** and is not implemented; the
`autoRegister` key is accepted, validated and recorded in the *global* config
only, because a committed project file that could switch it on would let
cloning a repository write to a contributor's machine-wide registry.

An entry is an absolute path plus a `kind` (`vault` or `bundle`), a canonical
`registeredAt` stamp, and an **`id` that is a slug of the directory name,
uniquified once at register time and never recomputed**. That last property is
the one worth remembering: hashing the path would re-key an entry the first
time a checkout moved, and a random identifier would make `okf unregister` and
`okf registry list` unreadable. A directory named `okf` is slugged from its
parent, so `~/okf` and `<project>/okf` do not all want the same name.

Unlike `okf.json` the registry is **strict JSON**, not JSONC. `okf.json` is
JSONC because a person writes it and their reasons are the half a reviewer
needs; the registry has exactly one writer, and a comment in it would be erased
by the next `okf register`. It is written sorted by id and atomically — a temp
file, then a rename — so an interrupted write cannot leave half a registry
where the readable one was. Reading it never writes it.

# Scope is project-only by default

Search sees the project's bundles and nothing else unless configuration or a
flag opts registry entries in. The reason is team determinism: a query must
return identical results on every developer's machine and in CI. A search
that silently widened to include whatever happened to be on one contributor's
laptop would produce answers nobody else could reproduce — and the same
reasoning is what makes [search
semantics](search-semantics.md) refuse a clock and a network call.

Four scopes exist, spelled the same as a `--scope` flag and as the
`search.scope` setting:

| Scope | Sees |
| --- | --- |
| `project` (default) | the vault the walk-up found, else the personal vault |
| `personal` | the personal vault (`OKF_HOME`, else `~/okf`) |
| `registered` | every registry entry whose path still exists |
| `all` | the project scope plus the registry, de-duplicated |

`--scope personal` works whether or not the personal vault has been
registered. That is not the registry making an exception: discovery has always
known where the personal vault is, so it resolves it directly rather than
looking it up.

`okf mcp` takes the same flag, **at launch only** — a tool call can never
widen the scope its server was started with. Both surfaces resolve scope
through one function in `Okf.Core`, and a test asserts that the same query
returns byte-identical results from `okf search --json` and from the server's
`okf_search` at every scope.

A registered path that has gone missing is reported once on stderr and
skipped; it never fails a query, because a registry is per-machine state that
goes stale on its own. With more than one root in scope, human search output
prints each result's absolute path and counts the vaults, because two vaults
can hold the same bundle-relative path.

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

`okf.json` is named `.json` and **parsed as JSONC**: comments and trailing
commas are accepted, deliberately. A severity promotion without a stated
reason is a promotion nobody can review, and the reasons are the half of a
configuration file a reader actually needs. The cost is real and is accepted
rather than hidden — a strict JSON editor or schema will flag a file the tool
reads happily — and `okf init` writes the config with its reasons in it, so
the convention is exercised from the first commit rather than discovered by
reading the parser.

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

[^decisions]: okf-net — Architecture Decisions, §6, Q4, the `okf lint` milestone, and the vault registry and scope (work item #43).
[^prd]: okf-net — Product Requirements, CLI-1 through CLI-4, CORE-13.
