---
type: Concept
title: Library, CLI, and MCP Layering
description: All logic lives in Okf.Core; the CLI and the MCP server are thin adapters and neither is the core.
tags: [okf-net, architecture, cli, mcp, layering]
generated: { by: claude-fable/5, at: 2026-08-14T21:42:00-05:00 }
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

**All logic lives in `Okf.Core`.** The `okf` CLI is a thin wrapper over it.
The MCP server is a thin protocol wrapper launched as `okf mcp`, a subcommand
of the same binary. Neither adapter is the core, and neither may hold
behaviour the other lacks.[^decisions]

```text
Okf.Core   parse · validate · trust · staleness · index · search · discovery
   │
   ├── Okf.Cli    okf lint | index | search | mcp   (built)
   │              okf inbox | verify | register | unregister | init
   └── okf mcp    okf_list · okf_search · okf_read, over stdio (built)
```

The diagram is the designed surface, not an inventory. `lint`, `index`,
`search`, and `mcp` exist today; the rest of the verb list and the skills are
later milestones, in the build order Core → index → search → MCP → skills.
Everything below describes the contract each one is being built against.

# Why the library is the core

A git hook and a CI job need a plain process: start, read files, exit with a
meaningful code. They do not want to speak JSON-RPC to a server, and they
certainly do not want to start one. MCP is an always-available *adapter*, not
the centre of gravity.

The corollary matters more than the diagram. Every requirement is satisfied
by the library and is unit-testable with no process boundary — which is why
the search engine returns *data* rather than formatted text, and why the CLI
and the MCP `search` tool can render the same result set differently without
either becoming a second implementation.[^prd]

Scope parity is asserted, not hoped for: the same query in the same working
directory must return identical results through the CLI and through MCP,
because both call [the same vault
resolution](vault-registry-and-config.md).

# What the CLI owes its callers

- **One process, no daemon, no network, no model call.** The whole MVP
  surface runs with networking disabled.
- **Exit codes as contract.** `0` clean, `1` diagnostics at error severity or
  drift under `--check`, `2` usage or environment failure. Warnings alone
  never change the exit code unless they were promoted.
- **Machine-readable output.** Every diagnostic carries a stable rule id,
  severity, path, and line, and `--json` emits it as the contract for CI
  annotations.
- **A single self-contained binary.** NativeAOT, linux-x64, roughly four
  megabytes, no .NET SDK required to run it — so the implementation language
  never leaks to a consumer. The `PublishAot` flag is set on the CLI project
  so the analysers run on *every* build: an AOT-hostile dependency fails the
  build rather than the release.

# What MCP deliberately does not do

The MVP MCP surface is **read-only**: `okf_list`, `okf_search`, `okf_read`.
Stamping and index generation stay CLI operations. Skills teach agents to call
the CLI, which is the settled industry answer after several years of servers
accreting tools nobody invoked.

The protocol layer is hand-rolled — newline-delimited JSON-RPC 2.0 over four
methods, no dependency — for the same reasons the CLI's argument parsing is:
the surface is small and stable, the exit-code contract is ours to keep, and
the binary must publish NativeAOT-clean. The official C# SDK was measured
against both gates before that call was made, and the adapter costs the
binary about a tenth of a megabyte.

Containment is enforced at the boundary: every path accepted or returned is
confined to a resolved bundle root, and traversal outside it is rejected.

[^decisions]: okf-net — Architecture Decisions, §4.
[^prd]: okf-net — Product Requirements, §2.1, §2.2, §2.3.
