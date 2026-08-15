# okf-net's custodian

This directory is the **custodian machinery** for the `okf-net` bundle: the
skills that maintain it, the recipe that configures them, and the one check
that `okf lint` structurally cannot perform. It sits beside the bundle rather
than inside it, and the bundler strips it on the way out, so a consumer
receives readable markdown that runs nothing.

Instantiated per decisions.md §1: *custodian machinery lives in the project
repo beside the bundle, git-hook/CI triggered — but the shared toolset is
referenced by version, never vendored per project.* okf-net is an unusual
consumer of itself, in that the toolset and the vault are the same checkout;
the references below are therefore repo paths where another project would
carry a released version.

## What is here

| File | What it is |
| --- | --- |
| `recipe.json` | The recipe: which skills apply, the search seeds an enrichment pass starts from, the exact commands, and what triggers each. |
| `check-manifest.py` | The capture manifest's invariants, checked in CI. Python 3 stdlib only, read-only, no network. |

Both `recipe.json` and the vault's `okf.json` are named `.json` and parsed as
**JSONC** — comments and trailing commas are accepted. A configuration file is
the team contract, and a setting without a stated reason is a setting nobody
can review.

## The skills

The custodian is not a program. It is two prose skills, shipped by this
repository and **referenced by path, never copied here** — a vendored copy is
a copy that drifts:

- [`skills/okf-capture/SKILL.md`](../../skills/okf-capture/SKILL.md) —
  capture. Something was just learned and belongs in the bundle: search the
  bundle first, apply the capture-versus-cite test, drop what cannot defend
  itself into `okf/raw/` with an entry in the capture manifest, write the
  concept and cite by key.
- [`skills/okf-custodian/SKILL.md`](../../skills/okf-custodian/SKILL.md) —
  maintenance. A capture is waiting in `raw/`, or the bundle needs enrichment,
  index regeneration, lint clearing, or a `log.md` line.

An agent working on this vault loads the skill by name; nothing here duplicates
their content, and a change to either lands in one place.

## What runs where

Honest scoping, because the alternative is a directory that claims a robot:

| Trigger | What runs | What it does not do |
| --- | --- | --- |
| `pre-commit` hook | `markdownlint-cli2` on staged markdown, and `check-manifest.py` when anything under `okf/raw/` is staged | Does not lint the vault — `okf lint` is a CI gate, kept out of the hook so a commit never waits on a `dotnet run`. |
| CI, `dogfood` job | `okf lint okf/`, `okf index --check`, `check-manifest.py` | Does not write. Every one of the three reports and exits; none of them repairs anything. |
| Enrichment | The two skills above, invoked by a person opening a session | Nothing scheduled, and no agent watching `stale_after`. The staleness-refresh loop is a later milestone. |

The gates are mechanical. The enrichment they gate is still human-initiated,
and saying otherwise in a README is how a project ends up believing it has
automation it does not have.

## The manifest check

`okf lint` walks a **bundle root**, and `okf/raw/` sits outside every one of
them by construction, so no diagnostic the linter has can reach the capture
manifest. `check-manifest.py` covers the part of that gap that is mechanically
decidable today:

1. `raw/manifest.json` parses as JSON, at `manifestVersion` 1, with a
   `captures` array. **A manifest that does not parse is reported and left as
   found** — never rewritten to make it parse.
2. Every entry carries `id`, `form`, `files`, `capturedAt`, `capturedBy` and
   an `ingestion` key; the `id` is a unique `<YYYY-MM-DD>-<slug>`, the `form`
   is `flat` or `packet`, and the timestamps are ISO 8601 with an offset.
3. Every `files[].path` stays inside `raw/`, exists on disk, and **hashes to
   its recorded `sha256`**. This is the immutability check: an ingested
   artifact that changed is the one failure the whole capture convention
   exists to catch.
4. A `flat` capture is one file named `<id>.<ext>`; a `packet` capture's files
   all live under `<id>/`.
5. **Nothing sits in `raw/` unrecorded.** A file no entry claims has no
   original URL, no hash, and no place in the custodian's work queue.
6. An entry whose `ingestion` is not `null` carries `at`, `by`, and a
   non-empty `concepts` array, and **every path in it names a file that
   exists** — vault-root-relative, not `raw/`-relative, because one capture may
   be ingested into more than one bundle.

What it deliberately does not check: that the concept is a *good* rendering of
the artifact, or that the artifact still matches its `originalUrl`. The first
is the prose layer, which is the custodian's job and not a script's; the second
needs the network, which no gate here is allowed to touch.

Run it by hand from the repo root:

```sh
python3 okf/custodian/check-manifest.py
```

Exit codes mirror `okf lint`: 0 clean, 1 violations, 2 usage or environment
failure. With no manifest present and an empty `raw/`, it reports that nothing
has been captured and exits 0 — the ordinary state of a vault before its first
capture.

## Related concepts

The bundle documents this model rather than repeating it:

- [The custodian model](../bundles/okf-net/practices/custodian-model.md)
- [Provenance — capture versus cite](../bundles/okf-net/format/provenance-capture-vs-cite.md)
- [Tagging discipline](../bundles/okf-net/practices/tagging-discipline.md) —
  the human face of `lint.tagRegistry` in `okf.json`
