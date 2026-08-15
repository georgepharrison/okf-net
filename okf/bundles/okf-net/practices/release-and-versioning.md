---
type: Playbook
title: Release and Versioning
description: Conventional commits drive semantic-release, and main ships release candidates until the toolset reaches 1.0.
tags: [okf-net, release, versioning, semantic-release, conventional-commits, playbook]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: releaserc
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/.releaserc.yml
    title: okf-net — semantic-release configuration
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: agents-md
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/AGENTS.md
    title: okf-net — Agent Instructions
    author: "human:ringo"
    last_modified: 2026-08-14
---

Every non-merge commit follows **Conventional Commits v1.0.0**, enforced by a
`commit-msg` git hook and re-checked in CI across the whole pushed range
rather than only at the tip.[^agents-md] The commit history is therefore not
just a log: it is the input that decides the next version number.

`semantic-release` runs on the default branch and derives the bump from the
commit types — `feat` minor, `fix`/`docs`/`refactor`/`perf`/`ci` patch — then
cuts the tag and writes the GitLab release notes.[^releaserc]

# Pre-1.0: main is a prerelease branch

`main` is configured as a **prerelease branch with the `rc` channel**, so
every merge ships an installable release candidate without committing to API
or format stability yet. When the toolset reaches a stable 1.0, the flip is:
make `main` a plain release branch, add a `dev` branch carrying the
prerelease channel, and delete the placeholder described below.

# The release-branch trap

`semantic-release` requires at least one **release-type** branch — a plain
name, no range, no prerelease — to exist on the remote, or it fails outright
with `ERELEASEBRANCHES`. Two ways to think you have one when you do not, both
verified empirically on this repository:

1. An unmatched maintenance glob expands to nothing and does not count. This
   is intended upstream behaviour, not a bug.
2. A branch named like `1.x` *does* match the maintenance glob but is
   classified as a **maintenance**-type branch, which also does not count
   toward the minimum. Only a plain-named branch does.

Hence `stable`: a placeholder release-type branch, pushed, pointing at main,
and never built — the release job runs on `main` only. It exists to satisfy
the minimum, and it is the first thing to delete at the 1.0 flip.

# Distribution

Today the `release` job does exactly one thing: run `semantic-release`, which
cuts the prerelease tag and writes the GitLab release notes. **No artifact is
published yet** — there is no packaging or publish job in the pipeline, which
is the still-open half of PRD Q10.

The intended end state is that a release also produces the self-contained
single-file binaries described in [library, CLI, MCP
layering](../toolset/library-cli-mcp-layering.md), a `curl | sh` install
path, and `Okf.Core` as a NuGet package on the self-hosted GitLab instance's
built-in registry, whose configuration is to be verified at first publish.

The point of shipping a binary rather than a runtime-dependent package is
stated in the goals and worth repeating here: **the implementation language
must never leak to a consumer.** Someone consuming an OKF bundle should not
need to know, or care, that the tool validating it is written in C#.

[^agents-md]: okf-net — Agent Instructions, "Commits".
[^releaserc]: okf-net — semantic-release configuration.
