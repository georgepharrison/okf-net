---
type: Playbook
title: Release and Versioning
description: Conventional commits drive semantic-release, main ships release candidates until 1.0, and every tag publishes a self-describing release the installer can verify.
tags: [okf-net, release, versioning, semantic-release, conventional-commits, distribution]
generated: { by: claude-fable/5, at: 2026-08-15T17:23:14Z }
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

# Two pipelines per release

A release is cut by two pipelines, not one, and they do different jobs.

The **branch** pipeline runs `semantic-release`, which decides the bump, pushes
the `vX.Y.Z-rc.N` tag and writes the GitLab release notes. Pushing that tag
starts the **tag** pipeline, whose `publish` job builds the artifact and
attaches it to the release the first pipeline just made.

That second pipeline used to do nothing at all. Tag pipelines were permitted,
but no job matched a tag, so every tag produced a pipeline that failed with
"no jobs in this pipeline" — a red pipeline as the ordinary outcome of a
successful release, which is how a team learns to stop reading them.

# Distribution

The `publish` job produces four artifacts under one package version. The first
is a self-contained NativeAOT `linux-x64` binary, of the kind described in
[library, CLI, MCP layering](../toolset/library-cli-mcp-layering.md); it goes
to the project's generic package registry as `okf/<version>/okf-linux-x64`.
The second is `okf-net-knowledge.tar.gz`: **this bundle**, packaged by the
binary the same job just built, so every release ships the toolset's own
knowledge as an installable OKF bundle. How it is packaged — and why the
archive is byte-reproducible from the tag — is [bundling and
distribution](../toolset/bundling-and-distribution.md). The release gains an
asset link per artifact.

The other two make a release **self-describing**. `latest.json` names the
version and, per asset, a relative path, a size and a `sha256` computed in the
job from the exact bytes it uploaded. `install.sh` is the installer that reads
it, uploaded from the repository so that the installer a release hands you is
the one that release was cut with, rather than whatever is on `main` today. The
installer is itself in the asset map: it cannot use its own digest, but a host
that republishes a release can, which is the only check the file people pipe
into `sh` would otherwise have.

# Installing

`curl -fsSL https://get.tychostation.dev/install.sh | sh` fetches
`latest.json`, verifies the binary against the digest in it, and installs
atomically to `~/.local/bin/okf`. `--version` pins a release, `--dry-run`
reports without writing, and re-running is safe. Redirects are followed, but an
`https` base URL is only ever followed to `https` — a single hop down to
cleartext would let one party write both the binary and the digest it is
checked against.

The host it names is an internal nginx that pulls each release from the
package registry and serves it read-only. **It resolves only inside Ringo's
network**, and that is a deliberate stopping point rather than an oversight: an
unauthenticated host serving a script people pipe into `sh` needs auth, rate
limiting and a signed manifest before it faces the internet, and none of those
exist yet.

The manifest is **unsigned** for now, which is the same deferral [bundling and
distribution](../toolset/bundling-and-distribution.md) makes about
`okf-bundle.json`, made for the same reason. A digest proves the bytes match
the manifest; it proves nothing about who wrote the manifest. Signing is key
management, not hashing, and it is tracked with the public-exposure work.

The sync runs on the **host**, pulling from the registry, rather than on the
runner pushing to the host. A push needs a credential on the shared runner that
can write to the artifact host's filesystem; a pull needs only a read-only
registry token held by the host, and nothing needs inbound access to it at all.

**Written, not yet run.** Only a tag pipeline runs the job, and no tag has
been cut since it was written, so nothing above is observed behaviour — it is
what the job is built to do. No release published so far carries a binary,
which also means the artifact host has nothing to serve and the install
one-liner has nothing to install. What the local evidence does cover: the
pipeline is valid against the instance, the merged YAML confirms the job is
tag-only and every other job branch-only, the API endpoints it calls were
probed read-only, and the manifest the job writes was generated locally from
the same block and shown to round-trip through the installer's reader. What it
cannot cover is the job end to end. Treat the first tag as the test.

The version is the tag without its leading `v`, so a downloaded binary answers
`okf version` with the package version it came from, plus the short commit sha
as build metadata — an rc tag can be rebuilt, so the version alone does not
identify a binary.

Two properties are worth naming because the installer leans on both. The
download URL is **predictable** from the version alone, which is what lets
`latest.json` describe a release with a relative path and lets a version be
pinned by name. And attaching the asset link is **idempotent**: link names and
URLs must be unique within a release, so the job asks whether the link is
already there rather than posting blindly and swallowing the error — a rerun of
a tag pipeline is a normal thing to do, and the two new assets go through the
same loop as the first two.

Still open from PRD Q10: `Okf.Core` as a NuGet package on the instance's
built-in registry, and builds for platforms other than glibc `linux-x64`.

The point of shipping a binary rather than a runtime-dependent package is
stated in the goals and worth repeating here: **the implementation language
must never leak to a consumer.** Someone consuming an OKF bundle should not
need to know, or care, that the tool validating it is written in C#.

[^agents-md]: okf-net — Agent Instructions, "Commits".
[^releaserc]: okf-net — semantic-release configuration.
