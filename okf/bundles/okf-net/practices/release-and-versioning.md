---
type: Playbook
title: Release and Versioning
description: Conventional commits drive semantic-release, dev ships release candidates and main ships stable versions, and every tag publishes a self-describing three-platform release the installers can verify.
tags: [okf-net, release, versioning, semantic-release, conventional-commits, distribution]
generated: { by: claude-fable/5, at: 2026-08-16T00:00:00Z }
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

`semantic-release` runs on the two release branches and derives the bump from
the commit types — `feat` minor, `fix`/`docs`/`refactor`/`perf`/`ci` patch —
then cuts the tag and writes the GitLab release notes.[^releaserc]

# Two branches: dev proposes, main releases

Merge requests target **`dev`**, which is configured as a prerelease branch on
the `rc` channel: every merge there cuts a `vX.Y.Z-rc.N` tag and an installable
release candidate. **`main`** is a plain release branch — merging to it cuts a
stable `vX.Y.Z` — and it only ever advances by an explicit promotion of `dev`,
a fast-forward or merge performed when the release is wanted.[^releaserc]

That split makes a stable version a *decision* rather than a side effect.
Nothing is scheduled and nothing promotes itself; the branch graph is where the
decision to ship is recorded. The CI rule names both branches literally rather
than deriving one from `$CI_DEFAULT_BRANCH`, because the default branch is one
of the two and naming only it would quietly mean "main only".

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

Before the 1.0.0 flip this repository had no release-type branch at all — `main`
was itself a prerelease branch — so it carried a placeholder called `stable`,
pushed and never built, purely to satisfy the minimum. `main` is now that
plain-named branch, so the placeholder is gone. The traps are kept written down
because the pressure to "tidy up" the unmatched maintenance glob recurs, and
the glob is not what satisfies the requirement.

# What the flip computed

Flipping `main` to a plain release branch does **not** continue from
`v1.0.0-rc.35`: prerelease tags establish no previous release on the release
channel, so semantic-release reports no previous release, takes the whole
history, and computes **1.0.0** — with release notes covering every commit the
project has. Afterwards, `dev` resumes from the stable version it now sees: one
`fix:` on `dev` past `v1.0.0` computes `1.0.1-rc.1`. Both numbers were measured
before the flip by running `semantic-release --dry-run` against a writable
mirror of the remote, with the second future manufactured in the mirror — a
fake `v1.0.0` tag and a `dev` branch off it — because that state does not exist
until the flip has already happened.

# Two pipelines per release

A release is cut by two pipelines, not one, and they do different jobs.

The **branch** pipeline runs `semantic-release`, which decides the bump, pushes
the tag — `vX.Y.Z-rc.N` from `dev`, `vX.Y.Z` from `main` — and writes the GitLab
release notes. Pushing that tag starts the **tag** pipeline, whose `publish` job
builds the artifacts and attaches them to the release the first pipeline just
made. The `publish` job is identical either way: it reads the version out of the
tag, so a release candidate and a stable release are published by the same
code path.

That second pipeline used to do nothing at all. Tag pipelines were permitted,
but no job matched a tag, so every tag produced a pipeline that failed with
"no jobs in this pipeline" — a red pipeline as the ordinary outcome of a
successful release, which is how a team learns to stop reading them.

# Distribution

The `publish` job produces eight artifacts under one package version, and the
release gains an asset link per artifact.

Three are binaries, of the kind described in [library, CLI, MCP
layering](../toolset/library-cli-mcp-layering.md), and they are not built the
same way. `okf-linux-x64` is NativeAOT, about 6 MB. `okf-osx-arm64` and
`okf-win-x64.exe` are trim-safe self-contained single files, about 15 MB each.
The split is forced by where the runner is: NativeAOT compiles through the
**host's** native toolchain, so a Linux runner cannot produce a Mach-O or a PE
image, and this instance has no Mac or Windows runner. PRD Q10 wrote that
fallback down in advance — trim-safe self-contained, paying size and cold start
— expecting a language feature to trigger it; what triggered it was geography.
The trim analyzer still runs, so Q10's zero-warning finding is re-checked per
platform on every release rather than assumed to travel.

The fourth is `okf-net-knowledge.tar.gz`: **this bundle**, packaged by the
binary the same job just built, so every release ships the toolset's own
knowledge as an installable OKF bundle. How it is packaged — and why the
archive is byte-reproducible from the tag — is [bundling and
distribution](../toolset/bundling-and-distribution.md).

The fifth is `okf-skills.tar.gz`: the three agent skills of [the custodian
model](custodian-model.md), as a deterministic tar of `skills/*/SKILL.md` —
sorted by name, one fixed mtime, uid and gid zeroed, `gzip -n`, so a rerun of a
tag pipeline produces the same digest. No installer fetches it, because every
binary **embeds** the same three files and `okf skills install` writes them from
inside the image; the archive is for a reader who wants the prose without the
binary — an agent host okf-net does not know about, or a project vendoring the
files under its own `skills/`.

The last three make a release **self-describing**. `latest.json` names the
version and, per asset, a relative path, a size and a `sha256` computed in the
job from the exact bytes it uploaded; it has been a map keyed by asset name
from the start, so three platforms and a second archive were more entries
rather than a new shape.
`install.sh` and `install.ps1` are the installers that read it, uploaded from
the repository so that the installer a release hands you is the one that
release was cut with, rather than whatever is on `main` today. Both are
themselves in the asset map: neither can use its own digest, but a host that
republishes a release can, which is the only check a file people pipe into a
shell would otherwise have.

Only the Linux binary can be executed by the job that builds it, so only it
gets the self-version assertion — publish, run it, refuse to publish if what it
reports is not the version the package will claim. The other two get the one
static check worth having: `file`'s output is **grepped**, not merely printed,
because a publish that produced an ELF for a Windows runtime identifier prints
into a green log and reaches a tester's machine.

# Installing

`curl -fsSL https://get.okf.tychostation.dev/install.sh | sh` covers Linux and
macOS; `irm https://get.okf.tychostation.dev/install.ps1 | iex` covers Windows.
Each fetches `latest.json`, selects the asset for the machine it is running on,
verifies it against the digest in the manifest, and installs atomically — to
`~/.local/bin/okf`, or to `%LOCALAPPDATA%\okf\bin\okf.exe` with the user
`PATH` updated. Neither needs root or Administrator. Pinning a version,
reporting without writing, and re-running safely all work the same on both.

Both then run `okf skills install`, which downloads nothing: it writes the
embedded skills to `~/.local/share/okf/skills` and into Claude Code's or pi's
skill directory when the machine already has one. A failure there is a warning
naming the command to re-run and never a failed install — the binary is
verified and in place by then — and `OKF_SKIP_SKILLS=1` skips the step for
anyone who manages those directories themselves.

`install.sh` decides the platform in one `case` and refuses everything else by
name — an Intel Mac gets told that Rosetta translates the wrong way and where
to ask for an `osx-x64` build; anything non-Unix gets pointed at the PowerShell
installer. On macOS it also removes `com.apple.quarantine` from what it just
installed: `curl` sets that attribute on every download and Gatekeeper refuses
to run a quarantined binary that is neither signed nor notarised. The binary is
unsigned, because signing needs an Apple Developer account, and the workaround
is labelled as one rather than left as a surprise dialog.

Redirects are followed by `install.sh`, but an `https` base URL is only ever
followed to `https` — a single hop down to cleartext would let one party write
both the binary and the digest it is checked against. `install.ps1` cannot make
that promise: PowerShell has no equivalent of curl's `--proto-redir`. It
requires an `https` base URL and says so in the file.

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
one-liners have nothing to install. What the local evidence does cover: the
pipeline is valid against the instance, the merged YAML confirms the job is
tag-only and every other job branch-only, the API endpoints it calls were
probed read-only, all three binaries were built locally by `mise run
publish-all` and confirmed to be an ELF, a Mach-O arm64 and a PE32+ image, and
the manifest the job writes was generated locally from the same block and shown
to round-trip through the installer's reader. What it cannot cover is the job
end to end. Treat the first tag as the test.

Two further things nothing here has run. The macOS and Windows binaries are
cross-compiled on a Linux runner and cannot be executed by it, so their first
execution anywhere is a tester's. `install.ps1` has no acceptance suite at all,
because this pipeline has no Windows runner; it is static-analysed with
PSScriptAnalyzer and reviewed, which catches an unapproved verb and nothing
about Gatekeeper. `install.sh` is covered by 93 assertions per shell, run
under `sh` and — **only where it is installed** — under `dash`. That
distinction is not pedantry: a workstation without `dash` runs half the matrix
and still prints a green, so the `test-install` CI job fails outright when
`dash` is missing rather than skipping the lane, and the harness announces
`shells under test:` before its first case.

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
a tag pipeline is a normal thing to do, and every asset added since goes
through the same loop as the first two.

Still open from PRD Q10: `Okf.Core` as a NuGet package on the instance's
built-in registry; NativeAOT for macOS and Windows, which needs a runner on
each; code signing and notarisation; and `brew`/`winget` formulas, which want
both a public download URL and a signed artifact. An `osx-x64`, musl or
`linux-arm64` asset is one line in the publish job and one case label in the
installer, and is not built on speculation.

The point of shipping a binary rather than a runtime-dependent package is
stated in the goals and worth repeating here: **the implementation language
must never leak to a consumer.** Someone consuming an OKF bundle should not
need to know, or care, that the tool validating it is written in C#.

[^agents-md]: okf-net — Agent Instructions, "Commits".
[^releaserc]: okf-net — semantic-release configuration.
