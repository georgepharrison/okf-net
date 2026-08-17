---
type: Concept
title: Library, CLI, and MCP Layering
description: All logic lives in internal Okf.Core; the CLI and MCP server are thin adapters, and Core is not a distributed NuGet API.
tags: [okf-net, architecture, cli, mcp, layering]
generated: { by: "openai-codex/gpt-5.6-luna", at: 2026-08-17T19:32:41Z }
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

**All logic lives in internal `Okf.Core`.** The `okf` CLI is a thin wrapper over
it. The MCP server is a thin protocol wrapper launched as `okf mcp`, a
subcommand of the same binary. Core is explicitly non-packable and is not a
distributed NuGet API. Neither adapter is the core, and neither may hold
behaviour the other lacks.[^decisions]

```text
Okf.Core   parse · validate · trust · staleness · index · search
           discovery · bundle · site
   │
   ├── Okf.Cli    okf init | lint | index | search | inbox | verify   (built)
   │              okf register | unregister | registry               (built)
   │              okf capture | generated                            (built)
   │              okf bundle | site | skills | completion            (built)
   │              okf upgrade | help | version                       (built)
   └── okf mcp    okf_list · okf_search · okf_read, over stdio       (built)
```

Every verb above is built. `register`, `unregister` and `registry` are what
`okf search --scope` and `okf mcp --scope` read (see [vaults, registry, and
config](vault-registry-and-config.md)); scope resolution itself is one function
in `Okf.Core`, so the CLI and the server cannot disagree about which bundles a
scope names. `okf completion` came last and describes the rest: it prints a
bash, zsh, fish or PowerShell script generated from the same verb table the
binary dispatches on, so a completion cannot lag a verb — a table that is
missing one fails a test rather than a keystroke. The build order that got here
was Core → index → search → MCP → skills, then the post-MVP additions.

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

- **One process, no daemon, no model call, and one network path.** Every verb
  that reads or writes knowledge runs with networking disabled. The single
  exception is `okf upgrade`, which replaces the binary with a release it
  verifies against the published manifest; it is confined to one library file,
  reachable from no other verb, and never runs unless it is the verb you typed
  — so a lint run, an index check or an MCP session still cannot be failed by a
  network.
- **Exit codes as contract.** `0` clean, `1` diagnostics at error severity or
  drift under `--check`, `2` usage or environment failure. Warnings alone
  never change the exit code unless they were promoted.
- **Machine-readable output.** Every diagnostic carries a stable rule id,
  severity, path, and line, and `--json` emits it as the contract for CI
  annotations.
- **A single self-contained binary, on three platforms.** `okf-linux-x64` is
  NativeAOT at about 6 MB. `okf-osx-arm64` and `okf-win-x64.exe` are trim-safe
  self-contained single files at about 15 MB each — the IL trimmed, then
  bundled with a .NET runtime — because NativeAOT compiles through the
  **host's** native toolchain and the only runner here is Linux. None of the
  three needs a .NET SDK to run, which is the property that matters: the
  implementation language never leaks to a consumer. The `PublishAot` flag is
  set on the CLI project so the analysers run on *every* build, and the trim
  analyser runs on the other two, so an AOT- or trim-hostile dependency fails
  the build rather than the release. See [release and
  versioning](../practices/release-and-versioning.md).

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
