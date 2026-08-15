---
type: Concept
title: Bundling and Distribution
description: The bundler ships bundles/ and nothing else, deterministically, with a manifest attesting what shipped and which links now dangle.
tags: [okf-net, distribution, bundle, determinism, cli]
generated: { by: claude-fable/5, at: 2026-08-15T08:30:00Z }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/6041b28b6a9a3a6a19d0eb5a610c7bfd38589de1/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-15
  - id: prd
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/6041b28b6a9a3a6a19d0eb5a610c7bfd38589de1/docs/prd.md
    title: okf-net — Product Requirements
    author: "human:ringo"
    last_modified: 2026-08-15
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/3fcbb9f828c2f23d109c855ee403c3a4c81f3a96/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
---

A **bundler** is the custodian's counterpart on the way out: it packages a
bundle for consume-only distribution, so what a consumer receives is readable
markdown they never have to execute.[^decisions] `okf bundle [path] --out
<file-or-directory>` is that command, and the specification already fixes the
shapes it may produce — a git repository, a tarball or zip of the directory, or
a subdirectory of a larger repository.[^okf-spec] okf-net writes a `tar.gz` by
default, a `zip`, or a plain directory, which is also the git-repository shape
once it is committed.

# What ships, and what never does

What ships is `bundles/<name>/**`: the concepts, the generated `index.md`
files, `log.md`, the `about.md` files, `references/`, and non-markdown content
such as an attester script, which the format treats as first-class bundle
material.[^okf-spec]

What never ships is everything outside a bundle root — and it is not stripped
so much as never reached, because the walk starts at `bundles/`. That is the
payoff of putting the machinery *beside* the bundle rather than inside it:
`okf/custodian/`, `okf/raw/`, `okf.json`, `.markdownlint.yaml` and the vault's
repo-facing README are producer-side by construction. `raw/` in particular is a
**producer-side archive**: a consumer receives the `references/` concept
extracted from a captured artifact plus the original URL in its frontmatter,
never the captured bytes.

Inside a bundle root, two narrow rules apply. Dotfiles and dot-directories are
skipped — the same walk `okf lint` uses, so `.obsidian/` and `.DS_Store` are
already gone — and a short list of editor droppings is excluded by *name*
(`*~`, `*.swp`, `*.orig`, `*.bak`, and friends). The list is short on purpose:
a file whose name does not announce itself as junk gets packaged, because a
producer who put it in a bundle root meant it to be there.

# Deterministic, so the artifact can be checked

The same vault, packaged by the same version with the same stamp, produces a
**byte-identical** archive. A release artifact whose bytes change on every
rebuild cannot be checked against a published digest, and two people cannot
confirm they received the same knowledge.

What that costs, concretely: entries sorted by path, no directory entries,
every archive timestamp fixed at `1980-01-01T00:00:00Z` (not the file's mtime,
which a fresh clone rewrites; not the epoch, which a zip's MS-DOS timestamp
cannot hold), ownership `0:0` with empty names, and mode `0644` on everything.

One value in the output comes from a clock — the manifest's `generatedAt` — and
`--generated-at` pins it. The tag pipeline passes the commit's timestamp, so a
released archive is reproducible from the tag rather than from the minute the
runner started.[^decisions]

# The manifest is the attestation

Every distribution carries one `okf-bundle.json` at its **root**, outside every
bundle root. It records the manifest and spec versions, the generator as a §7
actor, the source vault's *name* (never its path), the stamp, the bundles
included, the links that now dangle, and a `sha256` for every file shipped.

Three things about it are decisions rather than details:

- **It sits outside the bundle root** for the README trap's reason. Every
  non-reserved file inside a bundle root is a concept that must carry
  frontmatter with a non-empty `type`;[^okf-spec] a manifest inside one would
  fail the conformance check it exists to support. A consumer who ignores or
  deletes it still holds a complete `cat`-readable bundle.
- **It is the one file it does not hash.** A manifest cannot record its own
  digest without a fixed point, and a self-attesting manifest proves nothing:
  the claim is only as strong as the channel it arrived over. The hashes exist
  for the mundane failures — a truncated download, a half-applied edit, a file
  added to an unpacked copy.
- **`okf bundle --verify <archive-or-directory>` re-checks it**, and exits 1 on
  a missing file, a changed file, *or* a file the manifest never listed. The
  third finding is the one an integrity check is easiest to write without.

# Cross-bundle links dangle, and the manifest says which

Packaging one bundle out of a vault whose bundles reference each other leaves
links pointing at concepts the consumer did not receive. The format's own
answer is that this is legal: a link whose target is not in the bundle is not
malformed, and consumers **must** tolerate it.[^okf-spec] So the bundler leaves
it as written, warns once per link on stderr, and records each one in the
manifest's `externalLinks` as `{from, to, bundle}`.[^decisions]

Vendoring the linked concept was the tempting alternative and is the wrong one:
a copy carries its own `generated`, `verified` and `stale_after`, all now
describing a document maintained somewhere else, and it cannot be re-verified
by the bundle that holds it. Refusing to package without a flag was rejected
too — it would block what the specification permits, which is the inverse of
this toolset's standing rule that defaults block only what the spec says.

Because a distribution keeps the vault's `bundles/<name>/` layout, a link
*between two packaged bundles still resolves*. The dangling case is precisely
the subset case, plus links into the stripped vault, and those are exactly what
the manifest lists.

# Linting what shipped, not what was there

`--lint` runs the linter over the packaged result the way a stranger sees it:
default severities, no `okf.json`, no vault around it. Linting the source
bundles would have answered a different question, because a producer's config
promotes rules — a vault that passes its own contract says nothing about how
the distribution reads to someone who has neither the config nor the vault.

For a subset carrying cross-bundle links, `OKF0309` (link leaves the bundle) at
info is the *expected* output, not a defect: it reports the same links the
manifest records. Only errors fail the run, which means only §11 conformance
does — the same line [the severity model](lint-severity-model.md) draws
everywhere else.

# Where it runs

`mise run bundle` packages this vault into `artifacts/` and lints and verifies
the result. The tag pipeline's `publish` job does the same with the binary it
just built and uploads `okf-net-knowledge.tar.gz` beside it, so every release
carries the toolset's own knowledge as an installable bundle — the second half
of what [release and versioning](../practices/release-and-versioning.md)
describes.[^prd]

[^decisions]: okf-net — Architecture Decisions, §1 and "the bundler milestone".
[^prd]: okf-net — Product Requirements, §5.
[^okf-spec]: Open Knowledge Format v0.2, §3, §6.1, §6.3, §11.
