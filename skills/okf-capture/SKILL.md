---
name: okf-capture
description: >
  Capture something just learned into an OKF knowledge bundle: search the bundle
  first, apply the capture-vs-cite test, drop artifacts in the vault's raw/ zone
  with a manifest entry, and write a conformant concept. Use when the user says
  remember this or write this up, when a link, PDF, or talk needs to outlive its
  URL, or when a session produced knowledge worth keeping.
---

# okf-capture

A vault decays two ways — a fourth document about the same thing, and a claim
nobody can trace — and every step below closes one of them.

## Doctrine

**Orient by disclosure, retrieve by search, open what you pick.** Read
`index.md` to learn what a bundle holds, `okf search` to find candidates, and
the concept files search pointed at.

**`raw/` is evidence; the bundle is knowledge.** A captured artifact is read
once, ingested into a concept, and frozen. It sits outside every bundle root,
so nothing indexes it, searches it, or ships it.

## The interface

`okf search`, `okf index`, and `okf lint` are how the vault is read; `okf
capture add` and `okf generated stamp` are how its two machine-owned records are
written. Run `okf <command> --help` for a command's options. Search is
tokenized, field-weighted, and returns a trust tier and a stale flag per hit, so
a hit can be judged before it is opened — reach for it in place of a text scan
of the directory. With `okf` absent from the PATH and no documented equivalent
in the project, stop and say so.

**Structure is the toolset's; prose is yours.** Hashes, timestamps, the
manifest's JSON and the `generated` stamp all have a command that writes them,
and every one of those commands is deterministic and refuses rather than
guesses. What no command can do is decide *what is worth keeping and what it
means* — write that, and let okf write the rest.

The layout it resolves against:

```text
<project>/okf/
  okf.json         # committed team contract: rule severities, tag registry
  bundles/<name>/  # bundle roots — the only thing that ships
  raw/             # drop zone + manifest.json, OUTSIDE every bundle root
```

## 1. Orient

```sh
okf search "<two or three words from the thing you learned>" --verbose
```

`--verbose` names the vault and the bundles it resolved, on stderr.

**Done when** you can name the vault path, the bundle you will write into, and
the concepts nearest your topic. Resolving nothing means there is no vault
here: ask whether one should exist.

## 2. Search before you create

```sh
okf search "<terms>" --limit 10
```

Read the hits — the results carry their paths — then take exactly one exit:

- **A concept already covers this.** Extend it. This is the ordinary outcome.
- **A concept is adjacent but distinct.** Write a new one, and say in your reply
  which concept it sits beside and why it is separate.
- **Nothing matches.** Write a new one, and say the search came back empty.

Two documents about one thing turn a knowledge base into a search problem, and
then into an abandoned directory.

**Done when** every result you did not open has been dismissed by title, and
your chosen exit is named in your reply.

## 3. Apply the capture-vs-cite test

One question classifies each source behind the concept:

> **If this source changed or vanished tomorrow, could the custodian still
> re-verify the concept?**

**Yes → cite it.** Repository code, a database you own, version-pinned library
documentation, a git tag, a permalinked commit. It gets a `sources` entry and
skips to step 6:

```yaml
sources:
  - id: ranking-docs
    resource: https://example.org/docs/v4.2/ranking
    title: Ranking functions, v4.2
    author: "team:example-search"
    last_modified: 2026-05-01
```

Pin a version wherever one exists — a tag, a commit SHA, a `/v4.2/` in the
path. An unpinned URL to a living document is a citation that quietly stops
describing what you wrote.

**No → capture it, then cite the capture.** A blog post, a conference talk, a
PDF, a page that will 404 in eighteen months.

**Done when** every source behind the concept is on one side of the test.

## 4. Capture into `raw/`

`raw/` is the drop zone: `<project>/okf/raw/`, or `~/okf/raw/` in the personal
vault. It sits beside `bundles/`, outside every bundle root — a dropped `.md`
file inside a bundle would be a frontmatter-less concept and break §11
conformance on landing, and the bundler ships only `bundles/`, so originals
stay producer-side archive.

**Flat by default; a packet for lossy formats.** A PDF, a video, or an audio
file becomes a directory holding the original plus `extracted.md`, the readable
rendering. Everything else — HTML, a transcript, a text file — is one file, as
retrieved.

```text
okf/raw/
  manifest.json
  2026-08-14-ranking-functions-explained.html   # flat
  2026-08-14-okapi-bm25-paper/                  # packet
    original.pdf
    extracted.md
```

Name the item `<YYYY-MM-DD>-<slug>`, dated the day of capture — the name **is**
the manifest entry's id — then record it in the capture manifest, which is what
makes the item immutable later and the one place the original URL is guaranteed
to survive:

```sh
okf capture add okf/raw/2026-08-14-okapi-bm25-paper \
  --by <your-actor> \
  --url https://example.org/papers/okapi-bm25.pdf \
  --title "Okapi at TREC-3" \
  --source-last-modified 1994-11-01
```

**Never edit `okf/raw/manifest.json` by hand.** The manifest is the record that
proves the artifact did not change, and a hash an agent typed proves nothing.
`okf capture add` writes the entry and creates the file when there is none; it
edits the document in place, so no other entry moves by so much as a byte.

| What the entry records | Where it comes from |
| --- | --- |
| `id` | The item's own name, `<YYYY-MM-DD>-<slug>`: a flat file's name minus its extension, or the packet directory's name |
| `form` | `flat` for a file, `packet` for a directory |
| `files[].path`, `files[].sha256` | Every file of the item, hashed by okf. This is what "unchanged" is measured against |
| `capturedAt` | Now, RFC 3339 UTC at second precision — the one instant form okf writes |
| `capturedBy` | Your `--by` actor, §7 form: `<producer>/<version>` |
| `originalUrl` | `--url`. Omit only for material with no URL |
| `title`, `sourceLastModified` | `--title` and `--source-last-modified`. Optional, and worth the two seconds — they become the ingested concept's `title` and `sources[].last_modified` |
| `ingestion` | `null` until ingested; the custodian closes it with `okf capture close`, whose concept paths are relative to the vault root (`okf/`), not to `raw/` — one capture may land in more than one bundle |

Entries are append-only. A capture that has already been **ingested** is
refused, because ingestion is what freezes it, and new evidence is a new item
under a new date. One still waiting on ingestion is freely re-captured.

**Done when** `okf capture add ... --json` exits 0 and its report names your
item's id with `"ingested": false`.

## 5. Ingest what you captured

An artifact nothing cites is invisible: `raw/` is outside every bundle root, so
nothing searches it and nothing ships it. Turn the item into an ordinary
conformant concept under the bundle's `references/`, following
**okf-custodian**'s ingestion procedure, and cite that concept from the concept
you came here to write.

`references/` carries no meaning of ours; it is the plain §6.3 convention for
mirroring external material as first-class concepts. Immutability lives on the
`raw/` item through its manifest entry, never on a directory name.

**Done when** the item's `ingestion.concepts` names a file that exists in the
bundle.

## 6. Write the concept

```yaml
---
type: Concept
title: BM25 Field Weighting
description: One sentence. It becomes the index entry and the search snippet.
tags: [search, ranking]
sources:
  - id: okapi-bm25-paper
    resource: /references/okapi-bm25.md
    title: Okapi at TREC-3
    last_modified: 1994-11-01
---
```

`type` is the only key the format requires; `description` is the one practice
adds, because without it the concept renders as a bare link in its index and a
snippet-less search hit. Tags come from the bundle's registry when it has one
(`lint.tagRegistry` in `okf/okf.json`), spelled as the registry spells them. The
body is markdown under `#` headings — headings, lists, and tables are what a
reader and a retriever both work from.

`generated` is deliberately absent here: step 8 writes it with a command, at the
time the write actually finished.

**Done when** the frontmatter carries `type`, `title`, `description`, `tags`,
and a `sources` entry per cited source, and every tag appears in the registry
when one is configured.

## 7. Cite by key

A claim attributable to a source is footnoted with a label **equal to that
source's `id`**:

```markdown
Field weighting recovers most of what stemming would buy.[^okapi-bm25-paper]

[^okapi-bm25-paper]: Okapi at TREC-3, §3.
```

The label is the join key into `sources`; positional references break silently
the first time anything reorders the list, and agents reorder these lists
constantly. The `[^id]:` line is the note — the citation is the `[^id]` in the
prose, and a source declared but never referenced is a source you did not use.

**Done when** every `sources[].id` has at least one `[^id]` reference in prose,
and every `[^id]` label in the body matches a `sources[].id`.

## 8. Stamp `generated`

```sh
okf generated stamp <concept-path> --by <your-actor>
```

`--by` is your actor in §7 form — `<producer>/<version>` for an agent or tool,
`process:<id>` for an automated process. okf writes the instant, in RFC 3339 UTC
at second precision, because a `Z`-suffixed stamp sorts as text in the order it
sorts in time and a local offset records where you were sitting. It edits the
`generated` line and nothing else, so key order, quoting and the body all
survive.

Restamp on every write, a one-line edit included: `generated.at` older than the
content is a false freshness signal, and it feeds every drift and stale check
downstream. A fresh stamp puts the concept back on `okf inbox` — that is the
loop working, not a mistake.

**Done when** `okf generated stamp` exits 0 and reports your actor and the
instant it wrote.

## 9. Regenerate, lint, log

```sh
okf index <bundle-path>
okf lint  <bundle-path>
```

A new concept is a new index entry, and indexes are generated output. `okf lint`
exits 0 clean, 1 on diagnostics at error severity, 2 on a usage or environment
failure; read its summary line, which says how many rules ran, so a silenced
gate does not read like a passing one.

Where the bundle keeps a `log.md`, append a line for the addition — format in
**okf-custodian**.

**Done when** `okf lint <bundle-path>` reports 0 errors and 0 warnings, and a
second `okf index <bundle-path>` reports every index unchanged.

## Guardrails

- **`raw/` is read-only once ingested; all writing happens in the bundle.** New
  evidence is a new capture with a new manifest entry. An archive that can be
  edited cannot support the concept citing it.
- **`verified` belongs to a second actor.** You stamp `generated`; a human
  clears the concept with `okf verify`. An actor confirming its own work is a
  contradiction dressed as a signal, and `okf lint` reports it (`OKF0201`).
