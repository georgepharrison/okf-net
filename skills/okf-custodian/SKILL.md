---
name: okf-custodian
description: >
  Custodian for an OKF bundle: ingest raw/ captures into references/ concepts,
  enrich prose, keep tags and citations joined, regenerate indexes, clear lint,
  and append to log.md. Use when asked to maintain, enrich, or tidy a bundle,
  when captures are waiting in raw/, or when index drift or stale concepts need
  clearing.
---

# okf-custodian

A **custodian** maintains a bundle from beside it: ingestion, enrichment, prose,
indexes, and the log. The machinery lives in `<project>/okf/custodian/` and the
bundler strips it on the way out, so what a consumer receives is readable
markdown that runs nothing.

Two properties are yours alone to hold:

- **The prose layer.** Frontmatter is the few machine-queryable fields; the body
  is prose for humans and language models. Code checks the first. Nothing checks
  the second but you.
- **Surfacing, not landing.** Machine-derived insight is offered for review.

## Doctrine

**Orient by disclosure, retrieve by search, open what you pick.** Read
`index.md` to learn what a bundle holds, `okf search` to find candidates, and
the concept files search pointed at.

**`raw/` is evidence; the bundle is knowledge.** A captured artifact is read
once, ingested, and frozen. It sits outside every bundle root, so nothing
indexes it, searches it, or ships it.

## The interface

`okf search`, `okf index`, and `okf lint` are how the vault is reached; run
`okf <command> --help` for a command's options. Search is tokenized,
field-weighted, and carries a trust tier and a stale flag on every hit — reach
for it in place of a text scan of the directory. With `okf` absent from the
PATH and no documented equivalent in the project, stop and say so.

```text
<project>/okf/
  okf.json         # committed team contract: rule severities, tag registry
  bundles/<name>/  # bundle roots — the only thing that ships
  custodian/       # your machinery: skills, prompts, recipes, hooks
  raw/             # drop zone + manifest.json, OUTSIDE every bundle root
```

## Ingest: a `raw/` capture becomes a `references/` concept

This is the procedure **okf-capture** hands you, and the one to run when
something is waiting in `raw/`.

### 1. Take the entry

Read `okf/raw/manifest.json`. Entries whose `ingestion` is `null` are waiting;
each carries the `originalUrl`, `title`, and `sourceLastModified` you are about
to write into frontmatter. Read the artifact itself — the flat file, or a
packet's `extracted.md`, the original beside it being evidence rather than
reading material.

The manifest is the immutability record, so it is read as found: a file that
does not parse, or one whose recorded `sha256` no longer matches the artifact,
is reported to the user and left exactly as it is — rewriting it to make it
parse is how the record is lost. No manifest at all means nothing is waiting.

**Done when** you can name the item's id, its files, and its original URL.

### 2. Search before you write

```sh
okf search "<title words>" --type Reference --limit 10
```

Material already ingested gets extended, and a second path appended to that
entry's `concepts`, rather than a near-duplicate. `okf lint` reports title and
filename collisions (`OKF0303`), which is a late and blunt way to learn this.

**Done when** you have named the concept you will extend, or stated that the
search came back empty.

### 3. Write the concept

Under `bundles/<name>/references/`, as an ordinary conformant concept —
`references/` is the plain §6.3 convention, so a foreign bundle using the name
is never reinterpreted.

```yaml
---
type: Reference
title: Okapi at TREC-3
description: The paper that introduced BM25 and its k1/b parameters.
resource: https://example.org/papers/okapi-bm25.pdf
tags: [search, ranking]
generated: { by: claude-fable/5, at: 2026-08-14T21:02:11Z }
sources:
  - id: okapi-bm25-paper
    resource: https://example.org/papers/okapi-bm25.pdf
    title: Okapi at TREC-3
    last_modified: 1994-11-01
---
```

The top-level `resource` is the spec's canonical-URI field, carrying the
original URL: the distributed bundle ships this concept and not the archive, so
this is where a reader learns where the material came from. Name the raw item in
the body by its manifest id, which travels anywhere the bundle does:

```markdown
Captured 2026-08-14 as raw item `2026-08-14-okapi-bm25-paper`
(`okf/raw/manifest.json`).
```

A markdown link into `raw/` instead resolves outside the bundle root, is
reported as such (`OKF0309`), and dangles in every distributed copy.

The body is a faithful rendering of the material under `#` headings, lists, and
tables, carrying a `[^okapi-bm25-paper]` footnote on a claim it renders — the
`sources` entry is joined by key here like anywhere else. Analysis belongs in
the concept that *cites* this one.

**Done when** the concept carries `type`, `title`, `description`, `resource`,
`tags`, `generated`, and `sources`, the body names the raw item's manifest id,
and every `sources[].id` has a `[^id]` footnote in the body.

### 4. Close the entry

Set `ingestion` on that entry, leaving every other field as captured:

```json
"ingestion": {
  "at": "2026-08-14T21:02:11Z",
  "by": "claude-fable/5",
  "concepts": ["bundles/okf-net/references/okapi-bm25.md"]
}
```

`concepts` paths are relative to the vault root (`okf/`), not to `raw/` the way
`files[].path` is: one capture may be ingested into more than one bundle.

The item is now frozen: it is tracked by this entry rather than by any directory
name, and new evidence arrives as a new capture with a new entry. An extraction
that came out wrong is recaptured and re-ingested; the wrong one stays as the
record of what was actually retrieved.

**Done when** the manifest parses, `ingestion.concepts` names files that exist,
and `sha256sum` on every file of the item still matches its recorded hash.

## Enrich: writing and extending prose

- **Search before create**, every time, with the same three exits: extend what
  you found; write a new concept and say what it sits beside and why it is
  separate; or write a new one having said the search came back empty.

  ```sh
  okf search "<terms>" --limit 10
  ```

- **Tags come from the registry.** Where `okf/okf.json` sets `lint.tagRegistry`,
  that array is the vocabulary, spelled its way. A new tag is added to the
  registry in the same change, so the vocabulary is reviewed rather than grown
  by accident. With no registry configured, follow the tags already in the
  bundle.
- **Every concept carries a `description`** — it is the index entry and the
  search snippet.
- **Cite by key.** A claim attributable to a source is footnoted with a label
  equal to that `sources[].id`. The `[^id]:` line is the note; the citation is
  the `[^id]` in the prose, and `okf lint` wants the reference.
- **Tooling is wired declaratively**, through `executor.resource` and
  `attester.resource` in frontmatter, where a consumer can find it, version it,
  and let the bundler strip it.

**Done when** every `sources[].id` in every concept you touched has a `[^id]`
reference in prose, every `[^id]` label matches a `sources[].id`, and every tag
you wrote appears in the registry when one is configured.

## Stamp `generated`, on every write

```yaml
generated: { by: claude-fable/5, at: 2026-08-14T21:02:11Z }
```

`by` uses the §7 actor convention: `<producer>/<version>` for an agent or tool,
`process:<id>` for an automated process, `human:<id>` for a person — a prefix
that tells every consumer a person stood behind the content, so it stays with
people. `at` is RFC 3339 UTC at second precision —
`date -u +%Y-%m-%dT%H:%M:%SZ` — which is the one form okf-net writes, because
a `Z`-suffixed stamp sorts as text in the order it sorts in time and a local
offset records where you were sitting.

A one-line edit is a write. `generated.at` older than the content is a false
freshness signal, and it is the input to every drift and stale check
downstream.

**Verification is a second actor's act.** A non-generating agent or process may
record a machine-confirmed verification when it re-checked the claim against its
source — `okf verify <concept-path> --by process:<id>` — and only whoever did
the checking runs it. A human clears a concept with plain `okf verify`, which
stamps `human:<id>`. Either form refuses an actor equal to the concept's
`generated.by`, appends rather than replaces, and leaves the rest of the file
byte-identical — except on a frontmatter shape the line insertion cannot reach
(`verified` written as a flow list, or as a block mapping), where the whole
block is re-emitted and the content survives but the formatting moves. A bare
`{ by, at }` mapping is normalized into a one-element list on the way (§5.2).

Trust tier is derived from `verified` alone — no events is `unverified`, any
`human:` event is `human-reviewed`, otherwise `machine-confirmed`. There is no
tier field to set and no score to store.

**Done when** every concept you wrote carries `generated.at` equal to the time
of that write, and `verified` on each is exactly what it was before you started
unless you personally re-checked the claim.

## Publish: index, lint, log

```sh
okf index <bundle-path>            # write the index.md files
okf index <bundle-path> --check    # write nothing; exit 1 if any would change
okf lint  <bundle-path>
```

Indexes are output: `okf index` writes them after any change to a concept's
`title`, `description`, `type`, filename, or existence, and after a directory
appears or disappears. Hand-edited content in a generated index reads as
**drift** (`OKF0306`), promoted to error in most vaults we own.

A generated index carries `<!-- generated by okf -->` on its own line. An index
without that marker is a foreign, hand-written file — reported on, never blamed,
and replaced with a spoken warning by a plain `okf index` run. The marker
belongs only on generated output; removing it is how a bundle opts out.

`okf lint` exits 0 clean, 1 on diagnostics at error severity, 2 on a usage or
environment failure. Read the summary line rather than the exit code alone:

```text
Checked 21 files in 1 bundle (18 rules: 16 active, 2 hidden): 0 errors, 0 warnings, 0 infos.
```

It says how many rules ran, so a misconfiguration that silenced the gate does
not read like a passing gate; `--verbose` lists every rule with its effective
severity and the layer that set it. Warnings are this vault's configuration
saying what it cares about — clear them, or say why one stands. The severities
in `okf/okf.json` are the team contract; defaults block only what the spec
requires, and everything above that line is a deliberate local choice.

A concept that has reached its `stale_after` comes through the same gate
(`OKF0202`), and `okf search` flags it on every hit. `okf inbox` collects those
alongside everything else waiting on a person; **Refresh** below is the
procedure for clearing one.

Append to `log.md` for a notable update — a new concept, a significant rewrite,
a deprecation:

```markdown
# Directory Update Log

## 2026-08-14
* **Update**: Ingested [Okapi at TREC-3](references/okapi-bm25.md) and cited it from [BM25 field weighting](search/bm25-field-weighting.md).

## 2026-08-01
* **Initialization**: Created the bundle.
```

`##` headings are ISO `YYYY-MM-DD`, newest first, and `okf lint` checks both.
Today's heading is appended to when it exists; otherwise a new one opens at the
top. Past entries stay as written — the log is a record, not a summary. The
leading bold word is convention, and the prose after it is free.

**Done when** `okf lint <bundle-path>` reports 0 errors and 0 warnings,
`okf index <bundle-path> --check` exits 0, and every notable change of this
session has a `log.md` line.

## Surface, don't land

The chain that keeps machine-derived insight reviewable:

1. You write the concept and stamp `generated`.
2. Output you are not confident in carries `status: draft`, which makes the
   concept unacknowledged by definition — `generated.at` newer than the latest
   `verified[].at`, **or** `status: draft`. No acknowledgment field exists, and
   none is needed.
3. `okf inbox` lists what is waiting and `okf verify <concept-path>` clears an
   item with a human stamp. Both are live:

   ```sh
   okf inbox <vault-path>                  # grouped by reason, one row per concept
   okf inbox <vault-path> --format json    # the same rows as machine-readable records
   okf inbox <vault-path> --fail-if-any    # exit 1 when anything is listed
   ```

   `okf verify` is a person's command, not yours — it stamps `human:<id>` from
   `verify.actor` or the global git email. Name what you drafted in your reply
   and leave `status: draft` as the durable marker.
4. A custodian running in CI opens a merge request. The merge request *is* the
   inbox: reviewable, and — the part that matters — ignorable.
5. `log.md` records what was notable.

Drafted and derived material is presented as such. Where a concept carries a
`human:` verification and your change contradicts it, say so in your reply.

## Refresh: clearing a stale or drifted concept

Run this when `okf inbox` lists a concept under **Stale** or **Source drift**.
The product is a *draft* and an explanation of what moved; a person lands it.

### 1. Read the row, then fetch what changed

```sh
okf inbox <vault-path> --format json
```

Each record carries `staleAfter`, `generatedAt`, and — for drift —
`driftedSources[].lastModified`. Open the concept and read its `sources[]`.
Two kinds of source, two fetches:

- **Version-pinned** (`resource` names a release, tag, or version): fetch the
  release notes from the pinned version through to current.
- **A living URL**: fetch that URL's current content.

Use the retrieval the host gives you — a web-fetch tool where there is one,
`curl -sSL <url>` where there is a shell and network. Where the host has
neither, say so and stop: an expired date is a prompt to look again, and a
refresh that guesses at what changed is worse than the prompt.

**Done when** you can name, for each source, its pinned version or URL and the
material you fetched for it.

### 2. Draft the body, with the change and its consequences

The body gains a section, in this shape:

```markdown
# What changed since v4.11

* **Breaking** — `--strict` is now the default (v5.0.0).
* Footnote labels are matched case-insensitively (v4.14).

Affects [lint severities](../toolset/lint-severity-model.md), which documents
the pre-v5 default, and the `--strict` example in this concept's own body.
```

Name each change, and then name what in **this bundle** it touches: the
concepts that cite this one, and the claims that move. Analysis is the product
here. A faithful copy of the release notes is a *capture* — that is
**okf-capture**'s job, and this concept then cites it.

**Done when** the body carries a `# What changed since <old pin>` section, every
entry in it is attributable to material you fetched in step 1, and the impact
notes name concepts by link.

### 3. Restamp the draft

```yaml
status: draft
generated: { by: claude-fable/5, at: 2026-08-15T14:30:00Z }
stale_after: 2027-02-15
sources:
  - id: upstream
    resource: https://example.org/releases
    last_modified: 2026-08-10
```

- **`status: draft`** makes the concept unacknowledged by definition, which is
  what keeps it on the inbox after the other two reasons clear.
- **`generated.at`** is the time of this write.
- **`stale_after`** moves out **in this draft**. It is the sentence "somebody
  re-checked this today", and it becomes true when a person lands the draft.
- **`sources[].last_modified`** becomes what the fetch reported; that is what
  clears the drift reason.
- **`verified` stays exactly as it was.** A past human verification is a record
  of what a person read, and it stays even when your draft contradicts it —
  say so in your reply instead.

**Done when** `okf inbox <bundle-path>` still lists the concept, under
**Unacknowledged** and nothing else.

### 4. Surface it through CI, and land nothing

The refreshed draft leaves on a branch with a merge request open, exactly like
every other machine-derived insight (see *Surface, don't land* above). Landing
is a person's act, and it is two steps rather than one: they remove
`status: draft` and run `okf verify <concept-path>`. A verification alone
leaves a draft on the inbox, which is the marker working as intended.

Your reply names the concepts you drafted, what changed for each, and anything
you could not fetch.

**Done when** the drafts are on a branch with a merge request open,
`okf lint <bundle-path>` reports 0 errors and 0 warnings,
`okf index <bundle-path> --check` exits 0, `log.md` carries a line for the
refresh, and nothing was merged by you.

## Guardrails

- **`raw/` is read-only once ingested; all writing happens in the bundle.** New
  evidence is a new capture with a new manifest entry, and recorded hashes stay
  as recorded.
- **`verified` belongs to a second actor.** You stamp `generated` for content
  you wrote; a human clears it with `okf verify`. An actor confirming its own
  work is a contradiction dressed as a signal, and `okf lint` reports it
  (`OKF0201`).
