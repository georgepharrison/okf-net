---
type: Playbook
title: The Dependency License Gate
description: Every dependency must be Apache-2.0-compatible, checked against an allowlist over a generated SBOM in CI.
tags: [okf-net, licensing, dependencies, ci, compliance, playbook]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: agents-md
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/AGENTS.md
    title: okf-net — Agent Instructions
    author: "human:ringo"
    last_modified: 2026-08-14
---

okf-net ships under Apache 2.0, so **every dependency must be
Apache-2.0-compatible**: MIT, Apache 2.0, BSD, and the small set of licenses
the Apache Software Foundation treats as equivalent. No copyleft — GPL, LGPL,
MPL — and no source-available commercial licenses, in shipped code or in test
code.[^agents-md]

# Rules

- **Check the license of the exact version being added.** Packages change
  licenses between majors. FluentAssertions went proprietary at v8, and the
  standing instruction in this repository is to avoid it at *any* version and
  reach for Shouldly, or plain xunit `Assert`, when an assertion library is
  wanted.
- **Test dependencies are in scope.** They are the likeliest place for a
  license trap to enter unnoticed, precisely because they never ship.
- **Extend the allowlist deliberately, in its own reviewed commit, with a
  rationale that cites an authority.** Never inline an exception in the
  checker: an exception in code is invisible at review time and permanent by
  default.

# How it is enforced

GitLab CE cannot do this — SBOM license scanning and license-approval
policies are Ultimate features — so the gate is three ordinary steps, run
identically by `mise run licenses` locally and by the `licenses` CI job:

1. `dotnet tool restore` (the CycloneDX tool is pinned in the tool manifest),
2. `dotnet CycloneDX Okf.sln` to build an SBOM of the whole solution,
   deliberately *without* excluding test projects,
3. `python3 scripts/check-licenses.py`, a stdlib-only checker that fails on
   any component whose license is not covered by
   `scripts/licenses-allowed.json`.

Identifiers are matched case-insensitively, since SPDX identifiers are, but
are otherwise exact: no `+` or-later suffixes and no `LicenseRef-*`.

The job publishes the SBOM as an artifact **even when the check fails** — the
rejected SBOM is the one actually worth reading, and there is no license
scanning UI to fall back on.

# The one ruling on record

**MS-PL is allowlisted.** It is OSI-approved and permissive, the ASF lists it
under licenses similar in terms to the Apache License 2.0, and its single
restriction — redistributed MS-PL *source* stays MS-PL — cannot bite here,
because both MS-PL packages arrived through `Xunit.SkippableFact` and its
transitive `Validation` dependency and are test-only, never entering a
shipped binary.[^decisions]

The shape of that ruling is the template for the next one: name the license,
cite an authority, identify why the restriction cannot reach shipped code,
and write it down where the next person will look.

[^agents-md]: okf-net — Agent Instructions, "Dependencies must be Apache-2.0-compatible".
[^decisions]: okf-net — Architecture Decisions, "Ruling: MS-PL is allowlisted".
