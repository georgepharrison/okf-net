# okf-net — Architecture Spine

> **Status:** Architecture Spine, `v1.0.0-frozen`. The public API surface of `Okf.Core`
> was frozen by the [1.0.0 review](decisions.md#100-review-work-item-9-2026-08-15) on
> 2026-08-15; the two changes that review ordered landed in work item #30.
>
> **This document fixes invariants, not rationale.** Every rule below is distilled from
> [decisions.md](decisions.md), which stays the "why" log and wins wherever the two
> disagree. [prd.md](prd.md) stays the requirement-shaped "what". This spine is the
> consistency contract between them: the calls a future builder cannot read off compliant
> code.

## How this document is maintained

This section is written for the agent that will next update this file.

1. **Re-derive, never re-invent.** Read [decisions.md](decisions.md) and the code before
   editing an `AD`. *Done when* every claim you changed cites a decisions.md anchor or a
   file path that exists on `main`.
2. **Keep `AD` identifiers stable and append-only.** Amend an existing `AD`'s **Rule** in
   place when the decision was refined; add the next unused `AD-n` when the decision is
   new. *Done when* the identifiers still ascend with no gaps reused and no renumbering:
   `grep -oE '^### ~*AD-[0-9]+' docs/architecture.md` prints a strictly increasing sequence.
   The `~*` is what keeps a struck heading (rule 3) inside that sequence instead of
   dropping out of it.
3. **Strike a superseded `AD`; never delete one.** Wrap its heading in `~~` strikethrough,
   keep its **Binds** / **Prevents** / **Rule** lines intact for the record, and add a
   `**Superseded by:** AD-n` line. *Done when* the retired identifier is still present in
   the file and no later `AD` reuses it.
4. **Leave no placeholder.** No placeholder marker, no unfilled brace token, no unresolved
   cross-reference standing in for a rule. *Done when* this returns nothing outside fenced
   blocks:

   ```sh
   grep -nE '\bTBD\b|\bTODO\b|\bFIXME\b|similar to AD-[0-9]+' docs/architecture.md
   ```

5. **Every Stack row carries a version**, and every mermaid block renders. `mise run lint`
   checks neither, so check them: read the Stack column, and render every block —

   ```sh
   awk '/^```mermaid$/{n++;f=1;next} /^```$/{f=0} f{print > ("/tmp/ad-"n".mmd")}' \
     docs/architecture.md
   for m in /tmp/ad-*.mmd; do npx -y @mermaid-js/mermaid-cli -i "$m" -o "$m.svg"; done
   ```

   *Done when* every block produces an SVG, and you have **read** each one: a diagram is a
   claim, and an arrow that points at the wrong component is as false as a wrong **Rule**.
   Keep them portrait-ish — `flowchart TB` over `LR` — because these are reviewed on a
   phone.
6. **Regenerate the dogfood concept in the same change.** The bundle concept
   `toolset/architecture-spine.md` summarizes this document; when a section changes
   materially, update it and re-run `mise run cli -- index okf/bundles/okf-net`.
   *Done when* `mise run cli -- lint okf/` reports `0 errors, 0 warnings, 0 infos` and
   `mise run cli -- index --check okf/bundles/okf-net` reports `0 drifted`.
7. **Re-reconcile Deferred with the board.** It tracks issues, so it goes stale on its own.
   *Done when* `glab issue list --all --label phase:post-1.0` and `--label phase:polish`
   each list only open issues the table already carries, and the table names no issue that
   has since closed.

## Design Paradigm

okf-net is a **library-first .NET toolset** for OKF v0.2: one deterministic, offline,
side-effect-free core (`Okf.Core`) surrounded by thin adapters that only render what the
core returns — a CLI verb, an MCP tool, an HTML page, an archive entry. The paradigm is
ports-and-adapters in its honest, degenerate form: **the core decides, the adapters
format, and no adapter holds a rule of its own.** Beneath the core sits the format, and
the format — not the tool — is the interop layer, so distribution is **consume-only**: a
bundle is markdown a consumer can `cat`, and nothing okf-net produces requires okf-net to
read it. Above the core sits the split that gives the whole system its shape: **structure
is deterministic and machine-owned** — frontmatter round-trips, indexes, diagnostics,
archives, timestamps, site output, all byte-identical on every machine and on every
rerun — while **prose is written by an agent and reviewed by a person**, which is exactly
what the trust, staleness and acknowledgment families exist to track. The binary is a
NativeAOT single file, so the implementation language never leaks to a consumer.

## Inherited Invariants

These bind okf-net and okf-net cannot change them. OKF v0.2 is specified in
`okf/SPEC.md` in Google's
[knowledge-catalog](https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md);
section numbers below are that document's.

| Inherited | From | Binds here |
| --- | --- | --- |
| A bundle is complete as plain markdown, `cat`-readable and `git clone`-portable | SPEC §1 | No sidecar may be required to read a bundle; `okf-bundle.json` sits *outside* every bundle root (AD-35) |
| A concept identifier is its bundle-relative path minus `.md` | SPEC §2 | Search result `id`, `okf_read`'s argument, and `ingestion.concepts` all speak the same identifier |
| Directory structure is independent of the domain; no tree shape is mandated | SPEC §3 | Domain-first layout is okf-net's own convention layered on that freedom, never a claim about the spec (AD-2) |
| Three distribution shapes: a git repository, a tar/zip archive of the directory, a subdirectory of a larger repository | SPEC §3 | `okf bundle` emits `tar.gz`, `zip`, `dir` and adds no fourth (AD-33) |
| `index.md` and `log.md` are reserved names, never mandated files | SPEC §3.1, §8, §9 | Reserved files are never linted as concepts and never corpus entries (AD-26) |
| Markdown body plus YAML frontmatter; `type`, `tags`, `title`, `description` in §4.1 | SPEC §4 | The structured layer is frontmatter; the prose layer is the body (Design Paradigm) |
| Consumers SHOULD preserve unknown keys and MUST NOT reject unrecognized fields | SPEC §4.1 | Round-trip fidelity is a hard requirement, which is what rules out a typed serializer (AD-9, AD-10) |
| `sources[]` entries carry an `id` and a REQUIRED `resource`; a resource may be a population or scope descriptor, not only a path | SPEC §5.1 | `OKF0307`/`OKF0308` exist but cannot be errors by default, and only unambiguous paths are resolved |
| `verified` may be a bare mapping instead of a list | SPEC §5.2 | Normalization happens on read, before any tier is derived (AD-20) |
| Trust tiers are `unverified`, `machine-confirmed`, `human-reviewed` | SPEC §5.3 | The tier is derived from `verified` alone and never stored (AD-20) |
| `stale_after` is the freshness field | SPEC §5.5 | Staleness is `today >= stale_after`, with the comparison date injected (AD-25) |
| A link whose target is not in the bundle "is not malformed; it may simply represent not-yet-written knowledge" — consumers MUST tolerate it, and mark rather than drop | SPEC §6.1 | Broken links are `info`, never an error by default; the bundler records dangling links instead of refusing (AD-34); the site marks a refused destination rather than removing it (AD-38) |
| A relative link resolves relative to the document; absolute URLs are permitted | SPEC §6.2 | Link and source-resource resolution is document-relative first, then bundle-root-relative |
| `references/` is a content convention that explicitly covers code | SPEC §6.3 | `references/` carries no okf-net semantics, and a distribution ships non-markdown content (AD-17, AD-33) |
| Actors are `<producer>/<version>`, `human:<id>`, `process:<id>` | SPEC §7 | Every actor okf-net writes or validates is checked against this form (AD-20, AD-22) |
| An index is `#` sections of `* [Title](link) - description` bullets; only a bundle-root index may carry frontmatter, and only `okf_version`; consumers MAY synthesize a missing index | SPEC §8, §12 | `OKF0003` is a hard error, the generated marker is an HTML comment rather than frontmatter (AD-13), and `okf_list` synthesizes in memory |
| A log is `##` ISO date headings, newest first | SPEC §9 | `OKF0004` checks ordering and nothing stricter |
| Attested computations are wired by declarative `executor` / `attester` pointers | SPEC §10 | okf-net records and surfaces them and never executes one (AD-1) |
| Conformance is exactly two things: parseable frontmatter and a non-empty `type` on every non-reserved `.md` file | SPEC §11 | The entire default-severity model rests on this (AD-3) |
| Target framework `net10.0`; NativeAOT forbids reflection-based serialization and dynamic codegen on the published path | .NET 10 / NativeAOT | No serializer, no DI container, no reflective command framework, no MCP SDK (AD-8, AD-9) |
| `AssemblyVersion` holds only a numeric core; semver §10 excludes build metadata from precedence | .NET SDK / SemVer 2.0.0 | Two version strings are published deliberately (AD-41) |
| Zip stores an MS-DOS timestamp that cannot predate 1980; `System.Formats.Tar` PAX entries embed the writing process id | .NET BCL | The fixed archive timestamp is `1980-01-01T00:00:00Z` and the tar format is GNU (AD-36) |

## Invariants & Rules

Fifty-three numbered decisions, distilled from [decisions.md](decisions.md). Identifiers are
stable, ascend, and are never reused. Each **Source** link is the decisions.md entry that
argued it.

### Dependency direction

Who may depend on whom. This is a rule, not a picture.

```mermaid
flowchart TB
    callers["CI jobs · agent skills"] --> cli
    hosts["MCP hosts"] --> mcp
    cli["Okf.Cli<br/>verbs, args, JSON writers"] --> core
    mcp["MCP server<br/>okf mcp"] --> core
    core["Okf.Core<br/>all logic"] --> fs["filesystem<br/>markdown + YAML"]
    core -. never .-> cli
    core -. never .-> models["models"]
    core -. one file .-> upgrade["OkfUpgrade.cs<br/>okf upgrade only"]
    upgrade --> net["network<br/>https, digest-verified"]
```

### AD-1 — The format is the interop layer; okf-net is optional

- **Binds:** all
- **Prevents:** a toolset that makes itself load-bearing, so a bundle stops being readable
  without it.
- **Rule:** Nothing okf-net produces may require okf-net to consume. Consumption is
  read-only markdown; okf-net never executes an `executor`, an `attester`, or any other
  bundle-supplied code.
- **Source:** [decisions §1](decisions.md#1-where-agentstooling-live), PRD §1.2, §1.3

### AD-2 — A bundle root is never a repository root, and the vault layout is fixed

- **Binds:** `okf init`, the dogfood vault, every consuming project
- **Prevents:** the README trap — a frontmatter-less `README.md` inside a bundle root
  makes the whole bundle non-conformant under §11.
- **Rule:** Knowledge lives at `<project>/okf/`, holding `README.md`, `okf.json`,
  `bundles/<name>/`, `custodian/` and `raw/`. `okf init` refuses, with exit 2 and no
  writes, to scaffold a vault at or inside a bundle root. `okf init` also writes two files
  at the *project* root — the vault's parent, deliberately outside `okf/`: a marker-fenced
  context-pointer block in `AGENTS.md` (created if absent, spliced in place otherwise, never
  touching a byte outside the fence) and a one-line `CLAUDE.md` naming it (written only when
  none exists). Both opt out with `--no-agents-md`, both are skipped for the personal vault
  (its parent is the home directory, not a project), and `okf skills install --scope
  project` writes the identical pair.
- **Source:** [decisions §2](decisions.md#2-bundle-self-description--project-layout),
  [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15),
  [the AGENTS.md context pointer](decisions.md#proposed-decisions-the-agentsmd-context-pointer-work-item-56-2026-08-16)

### AD-3 — Defaults block only what the spec says

- **Binds:** every diagnostic, `okf lint`, `okf bundle --lint`
- **Prevents:** a linter whose opinions are indistinguishable from the format's
  requirements, which is how a tool starts rejecting valid bundles.
- **Rule:** Only the conformance range (`OKF00xx`) defaults to `error`. Every other
  diagnostic defaults to `warning`, `info` or `hidden`, and every additional block is
  consumer configuration.
- **Source:** [decisions §7](decisions.md#7-write-discipline--lint),
  [Q5](decisions.md#open-question-resolutions-2026-08-14)

### AD-4 — A foreign bundle is reported on, never blamed

- **Binds:** lint, index, search, MCP, site
- **Prevents:** okf-net's own conventions being enforced against a bundle it did not write
  — the ACC-1 acid test.
- **Rule:** Google's four reference bundles must lint at exit 0 under default severity,
  unmodified. Any *error* on them is a defect in okf-net. Warnings are permitted and
  expected.
- **Source:** PRD ACC-1, [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14)

### AD-5 — Exit codes are contract, and the mechanical gates never consult severity

- **Binds:** every command, every hook, every CI job
- **Prevents:** a hook or pipeline whose branch depends on how somebody configured a rule.
- **Rule:** `0` success · `1` diagnostics at error severity, or `--check` drift, or a
  `--verify` mismatch, or `okf inbox --fail-if-any` over a non-empty inbox · `2` usage or
  environment failure. Warnings alone never change the exit code unless promoted. Each of
  the mechanical gates is severity-blind: `okf index --check` returns 1 on drift regardless
  of `OKF0306`'s configured severity, and `--fail-if-any` reads the inbox rather than any
  diagnostic. `okf search` exits 0 on an empty result set.
- **Source:** PRD CLI-14, [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14),
  [exit codes](decisions.md#exit-codes-prd-wins-over-the-grep-convention)

### AD-6 — All logic lives in `Okf.Core`; adapters render and never decide

- **Binds:** `Okf.Core`, `Okf.Cli`, the MCP server
- **Prevents:** two surfaces answering the same question differently, one rule at a time.
- **Rule:** `Okf.Core` has no `ProjectReference` and depends on nothing in this repo.
  `Okf.Cli` references it one-directionally. Core returns data, never *adapter* output: the
  diagnostic report, the `--json` writers, the console summaries and the exit-code mapping
  are `Okf.Cli`'s and nothing else's. Artifact text the format itself defines — index
  markdown, scaffolded files, site HTML — stays Core's, because it is content rather than
  presentation. Every requirement is unit-testable without a process boundary.
- **Source:** [decisions §4](decisions.md#4-layering-library-is-the-core), PRD §2.1

### AD-7 — Offline and hermetic by contract

- **Binds:** `Okf.Core`, every CLI command, every lint rule
- **Prevents:** a gate that fails because a network did, and a library that cannot run in
  a hook or an air-gapped CI job.
- **Rule:** *(amended 2026-08-16 by #23, which added the one network path; the
  no-network-anywhere wording this replaces was true when written.)* No command invokes a
  model. Exactly one subprocess is launched anywhere in the toolset, and it is `okf
  verify`'s identity fallback: `OkfVerifyIdentity` reads `git config --global user.email`
  through an environment the caller controls, and every other entry point takes that read
  as an injected delegate. No other code path starts a process — which is why `raw/`
  immutability is detected by the manifest's recorded `sha256` and not by git. And there is
  **exactly one network path: `okf upgrade`, confined to `OkfUpgrade.cs`, never invoked by
  any other verb** — no startup check, no cached staleness banner, no background poll, no
  opt-out to configure — and injectable, so no test reaches a host and no gate can be
  failed by a network (AD-53).
- **Source:** PRD CLI-16, [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15),
  [acknowledgment loop](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15),
  [`okf upgrade`](decisions.md#proposed-decisions-okf-upgrade-work-item-23-2026-08-16)

### AD-8 — NativeAOT is both the distribution shape and a build-time gate

- **Binds:** `Okf.Cli`, every dependency considered for it
- **Prevents:** an AOT-hostile dependency being discovered at release time rather than at
  the commit that added it.
- **Rule:** `Okf.Cli` sets `PublishAot=true` in the csproj so the AOT analyzers run on
  every build; `Okf.Core` sets `IsAotCompatible=true`. A dependency that cannot publish
  AOT-clean fails the build, not the release. The published binary is self-contained and
  single-file, with `InvariantGlobalization=true`.
- **Source:** [decisions §3](decisions.md#3-languages),
  [Q10 / lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14)

### AD-9 — Third-party surfaces are hand-rolled or confined to one file

- **Binds:** YAML, argument parsing, the MCP protocol, markdown rendering
- **Prevents:** a dependency's model leaking into okf-net's public API, and a 3× binary
  for four JSON-RPC methods.
- **Rule:** A boundary library is confined to a single internal file (`YamlBridge.cs` is
  the precedent) and the public API exposes only okf-net's own model. Argument parsing and
  the MCP JSON-RPC loop are hand-rolled. Revisit the argument parser when the verb surface
  outgrows one readable `switch`, and the MCP loop when the surface grows past tools.
- **Source:** [YAML library](decisions.md#proposed-decision-decided-2026-08-15-review-9-yaml-library),
  [lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14),
  [mcp milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-mcp-milestone-2026-08-14)

### AD-10 — Frontmatter round-trips unchanged

- **Binds:** `OkfDocument`, `YamlBridge`, `OkfValue`, `okf verify`, `okf_read`
- **Prevents:** a producer's data being silently retyped or reordered by a tool that was
  only asked to read it.
- **Rule:** Parse and re-emit preserve every key including unmodelled ones, insertion
  order, and scalar form. YamlDotNet is used through its representation model and event
  emitter only, never its serializer. Frontmatter projected to JSON travels as source
  text; a YAML null becomes JSON null and nothing else is guessed. Duplicate keys are
  rejected — a deliberate deviation from the Python reference, pinned by test.
- **Source:** PRD CORE-2, [YAML library](decisions.md#proposed-decision-decided-2026-08-15-review-9-yaml-library),
  [port review flags](decisions.md#proposed-decisions-decided-2026-08-15-review-9-reviewer-flags-from-the-okfcore-port-review-2026-08-14)

### AD-11 — Diagnostic identifiers are `OKF####` with fixed category ranges

- **Binds:** every rule, every config file, every consumer of `--json`
- **Prevents:** a rule identifier that moves between categories, and a nickname being
  accepted where an identifier belongs.
- **Rule:** Conformance `00xx`, provenance `01xx`, trust `02xx`, hygiene `03xx`. The range
  is fixed by category and never reused. A kebab nickname is a label — it may appear in
  prose and beside the id in `--json` output, and is never a valid configuration key. The
  shipped catalog is `OKF0001`–`OKF0004`,
  `OKF0101`–`OKF0103`, `OKF0201`–`OKF0202`, `OKF0301`–`OKF0310`.
- **Source:** [Q2](decisions.md#open-question-resolutions-2026-08-14),
  [lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14),
  [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15) (`OKF0310`)

### AD-12 — Four Roslyn severities, no exemptions, and a typo is never silent

- **Binds:** `OkfSeverityResolver`, `okf.json`, CLI severity flags
- **Prevents:** a misconfiguration that silently disables a gate, and a special-case rule
  a consumer cannot govern.
- **Rule:** `hidden` < `info` < `warning` < `error`, any rule reconfigurable to any level.
  Later layers win per rule; lower layers still supply keys higher ones omit.
  `treatAllWarningsAsErrors` promotes only what currently resolves to `warning`. An
  unknown `OKF####` id in any layer throws and exits 2. A clean run and a disabled run must
  print different summaries, and `--verbose` lists every rule with its effective severity
  and the layer that set it.
- **Source:** [Q5](decisions.md#open-question-resolutions-2026-08-14), PRD CLI-6,
  [lint/search friction](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-lintsearch-friction-milestone-work-item-21-2026-08-14)

### AD-13 — A generated file is marked, and only a marked file can drift

- **Binds:** `okf index`, `OKF0306`, `okf init`
- **Prevents:** condemning a hand-styled foreign index as drift, and then overwriting it
  as an "update".
- **Rule:** Generated files carry `<!-- generated by okf -->` on its own line, read **only**
  at the position the renderer writes it — the first non-blank line, after a frontmatter
  block if there is one — never by scanning the file for the string. Deleting the marker is
  how a consumer opts out.
- **Source:** [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14)

### AD-14 — Drift is *drifted* plus *orphaned*; nothing is deleted

- **Binds:** `okf index`, `okf index --check`, `OKF0306`
- **Prevents:** a generator that removes a file a person may still want, and a `--check`
  that fails on a bundle okf-net never wrote into.
- **Rule:** Per directory the on-disk index is *created*, *unchanged*, *drifted*,
  *foreign* or *orphaned*. Only *drifted* and *orphaned* are drift. A missing index is not
  drift and a foreign index is not drift. An orphan is reported until a person deletes it.
  A plain `okf index` run may replace a foreign index but reports it as
  `replaced (was hand-written)` with its own summary count.
- **Source:** [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14)

### AD-15 — One index renderer serves generation, scaffolding, synthesis and drift

- **Binds:** `okf index`, `okf init`, `okf_list`, `OKF0306`
- **Prevents:** a scaffolded index that drifts the first time the renderer changes, and a
  synthesized listing that disagrees with a generated one.
- **Rule:** `OkfIndexGenerator` is the only writer of index text. Ordering is total and
  ordinal: concept `#` sections first (case-insensitive then ordinal by heading), the
  `Subdirectories` section always last, entries within a section ordered by link — by
  filename, because `title` is optional and may repeat. A subdirectory is listed only when
  it gets an index of its own.
- **Source:** [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14),
  [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15)

### AD-16 — Capture versus cite is settled by one test

- **Binds:** the capture skill, the custodian skill, every concept's `sources`
- **Prevents:** a knowledge base whose provenance evaporates when a blog post moves.
- **Rule:** Ask *if this source changed or vanished tomorrow, could the custodian still
  re-verify the concept?* Yes — cite it through `sources[].resource` with a
  `last_modified` and a version pin. No — capture the artifact into `raw/`, ingest it into
  an ordinary concept under the bundle's `references/`, cite the ingested concept, and carry
  the original URL forward on both the ingested concept (§5.1 `resource`) and the manifest
  entry (`originalUrl`). Lossy formats are captured as a packet, everything else flat.
- **Source:** [decisions §5](decisions.md#5-provenance-capture-vs-cite) (superseded on
  *where* by [Q3](decisions.md#open-question-resolutions-2026-08-14)), PRD SKILL-3

### AD-17 — `raw/` sits outside every bundle root; `references/` keeps plain §6.3 semantics

- **Binds:** the vault layout, search corpus, lint scope, the bundler, MCP
- **Prevents:** dropped artifacts becoming frontmatter-less concepts that break §11, and
  okf-net attaching private meaning to a spec-defined directory name.
- **Rule:** `<vault>/raw/` is the drop zone, a sibling of `bundles/`. Nothing under
  `bundles/<name>/references/` carries okf-net-specific semantics. Because `raw/` is
  outside every bundle root it is unreachable by search and by MCP and never ships in a
  distribution — each asserted by test rather than assumed — and index generation never
  reaches it either, for the same structural reason.
- **Source:** [Q3](decisions.md#open-question-resolutions-2026-08-14),
  [Q7 corpus rule](decisions.md#q7-resolution-search-semantics-2026-08-14)

### AD-18 — The capture manifest is the immutability record and the work queue, and is never repaired

- **Binds:** `okf/raw/manifest.json`, both skills, `check-manifest.py`, `OKF0310`,
  `okf capture add`, `okf capture close`
- **Prevents:** an agent "fixing" the one record that proves an artifact did not change.
- **Rule:** `<vault>/raw/manifest.json` is append-only. `files[].path` is relative to
  `raw/`; `ingestion.concepts` paths are relative to the vault root. An entry whose
  `ingestion` is null is uningested and freely re-capturable; immutability starts at
  ingestion. A manifest that will not parse, or a `sha256` that no longer matches, is
  **reported and left as found** — never rewritten to make it parse. The two verbs that
  write it, `okf capture add` and `okf capture close`, hold to the same terms (AD-52).
- **Source:** [capture and custodian skills](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-capture-and-custodian-skills-milestone-work-item-3-2026-08-14),
  [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15)

### AD-19 — One hash convention, one implementation

- **Binds:** the capture manifest, `okf-bundle.json`, `latest.json`, `install.sh`
- **Prevents:** two spellings of integrity in one repository, and a digest that has to be
  recomputed differently to be compared.
- **Rule:** `sha256`, 64 lowercase hex digits, lowercase-hex-encoded. Every file on disk is
  hashed by `OkfCaptureManifest.Sha256Of`; the archive verifier, which holds a stream and
  no path, uses the one-line stream form beside it (`OkfBundler.Digest`). `raw/`
  immutability and distribution integrity answer to the same convention and produce
  comparable digests.
- **Source:** [bundler manifest](decisions.md#okf-bundlejson-the-attestation-and-where-it-sits)

### AD-20 — Trust tier derives from `verified` alone, and no actor verifies its own generation

- **Binds:** `OkfTrustTier`, `okf verify`, `OKF0201`, both skills, the site
- **Prevents:** manufacturing the one signal §5.3 exists to carry.
- **Rule:** The tier reads only the normalized `verified` list — `generated`, `status` and
  `sources` never affect it. Absent or empty is `unverified`; any `by` starting `human:`
  (case-sensitive) is `human-reviewed`; otherwise `machine-confirmed`. `okf verify`
  refuses self-verification with exit 2 and writes nothing, checking every named concept
  before the first byte is written. The actor is validated as a §7 form and rejected for
  quotes, backslashes and control characters.
- **Source:** [decisions §7](decisions.md#7-write-discipline--lint), PRD CORE-6,
  [acknowledgment loop](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15)

### AD-21 — Acknowledgment is derived state, never a new frontmatter field

- **Binds:** `okf inbox`, the custodian skill, the scheduled CI job
- **Prevents:** inventing a field the format did not ask for, and a lint rule that scores
  "nobody has read this yet" as a defect.
- **Rule:** A concept is unacknowledged when `generated.at` is newer than the latest
  `verified[].at`, **or** `status: draft`, **or** there are no verification events at all
  behind a `generated.by` that is not a `human:` actor (an absent `by` counts as
  non-human). A verification carrying no readable `at` reads as acknowledged and can never
  win the latest-verification comparison. `okf inbox` reports one row per concept carrying
  its reasons — never per finding, which is `okf lint`'s job — and exits 0 whatever it
  finds, with `--fail-if-any` for the caller that wants a branch.
- **Source:** PRD CORE-15,
  [acknowledgment loop](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15)

### AD-22 — A human identity comes from configuration, then *global* git, then a refusal

- **Binds:** `okf verify`, `verify.actor`
- **Prevents:** stamping an agent's email as a person, which is the exact failure this
  repository would produce by default.
- **Rule:** The chain is `verify.actor` (project config, then global), then
  `git config --global user.email`, then a refusal naming both. The git read is **global
  only**, never repo-local, because repo-local identity is often an agent. A configured
  `verify.actor` that names a process or a tool is a configuration error, not a coerced
  `human:` stamp; `--by` is the machine-confirmation surface.
- **Source:** [Q6](decisions.md#open-question-resolutions-2026-08-14),
  [Q12](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15)

### AD-23 — Stamping edits the file's text, not a re-emitted document

- **Binds:** `OkfStamp`, `okf verify`
- **Prevents:** the first acknowledgment arriving as a diff that touches every frontmatter
  line in the vault.
- **Rule:** Verification inserts lines into the existing text, reaching the three shapes
  that occur — no `verified` key, a block sequence, and §5.2's one-line bare mapping — and
  falls back to the YAML emitter for anything else. The result is parsed back and required
  to hold exactly one more verification event ending with the actor just written;
  text surgery that produced something okf cannot read did not happen.
- **Source:** [acknowledgment loop](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15)

### AD-24 — One canonical timestamp form, one renderer, tolerant reading

- **Binds:** `okf init`, `okf verify`, the bundler, `latest.json`
- **Prevents:** three legal ISO 8601 spellings inside one repository, and a lexical sort
  that disagrees with a chronological one.
- **Rule:** Every **instant** okf-net writes is RFC 3339 UTC, `Z`-suffixed, second
  precision, rendered by `OkfCanonicalTimestamp` and nowhere else; the bare dates the
  format asks for (a `log.md` heading, the site's generation date) are the only exception.
  Reading stays tolerant of other spellings and nothing already written is rewritten. Per
  the .NET convention the static helper is named for what it *does* and the value type for
  what it *holds* — hence `OkfCanonicalTimestamp` (the one written form) beside
  `OkfLifecycleInstant` (whatever spelling is on disk, date or instant). The plain
  `OkfTimestamp` was retired by the 1.0.0 review, not reassigned.
- **Source:** [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15),
  [type reconciliation](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15)

### AD-25 — Timestamps compare at the coarser precision, and drift takes the wider ordering

- **Binds:** `OkfLifecycleInstant`, `OKF0103`, `OKF0202`, `okf inbox`
- **Prevents:** the inbox being quieter than the linter about the same file.
- **Rule:** `Compare` uses instants only when both sides have a time, so a bare date and a
  same-day instant are equal; the date compared is the one **written**, not the UTC one.
  Drift asks the wider question — later by instant **or** later by written date — so it is
  never weaker than the linter's date-only comparison. Staleness takes an injected `today`
  and an unparseable value is never stale and never an error.
- **Source:** [acknowledgment loop](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-staleness-refresh-and-acknowledgment-loop-work-item-7-2026-08-15),
  PRD CORE-7

### AD-26 — Search is deterministic lexical ranking with a total order

- **Binds:** `OkfSearchEngine`, `okf search`, `okf_search`
- **Prevents:** a query returning different results on two machines or in CI, which is
  what makes search unusable in a hook or a team contract.
- **Rule:** BM25 with `k1 = 1.2`, `b = 0.75` over field-weighted term frequencies (title
  ×3, `tags` ×2, `type` ×2, `description` ×2, body ×1) and a non-negative IDF. Collection
  statistics are computed over the whole resolved corpus, never the filtered candidate
  set. No clock, no network, no model call. The order is total: score descending, then
  bundle root, then bundle-relative path, both ordinal, with scores rounded to four
  decimals on output. Tokenization is Unicode-aware lowercasing split on non-alphanumerics,
  with no stemming, no stopwords and no minimum length.
- **Source:** [Q7](decisions.md#q7-resolution-search-semantics-2026-08-14),
  [scoring](decisions.md#scoring-as-shipped)

### AD-27 — The corpus is concepts only

- **Binds:** `OkfSearchEngine`, the site's counts and graph
- **Prevents:** ranking a table of contents above the content it lists, and double-counting
  every title.
- **Rule:** Only concept documents inside bundle roots are corpus entries. Reserved files
  (`index.md`, `log.md`) are excluded because an index is a *view* of the concepts; `raw/`
  is unreachable by AD-17. A concept whose frontmatter does not parse is not a corpus entry
  — it is already an `OKF0001` error and `okf lint` is the surface that says so. The same
  rule governs the site: reserved files become pages, but only concepts are counted,
  graphed and back-linked.
- **Source:** [Q7](decisions.md#q7-resolution-search-semantics-2026-08-14),
  [site milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-static-site-milestone-work-item-6-2026-08-15)

### AD-28 — The result contract is engine-agnostic and rendered once

- **Binds:** `okf search --json`, `okf_search`, the deferred vector layer
- **Prevents:** two renderings of one result set drifting apart a field at a time, and a
  contract that freezes today's engine.
- **Rule:** Nothing in a result record names BM25, tokens or fields: `score` is an opaque
  higher-is-better number and `matchMode` says only `all`, `any` or `filter`. Results are
  links-first — id and path, title, type, description, tags, score, trust tier, stale flag,
  a bounded snippet and the terms it matched — and never a full body. One writer
  (`SearchJson`) renders both surfaces, so parity is about
  bytes rather than fields. `--json` is a bare array; a metadata envelope is deferred and
  would be a versioned change.
- **Source:** [Q7](decisions.md#q7-resolution-search-semantics-2026-08-14),
  [mcp milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-mcp-milestone-2026-08-14),
  [1.0.0 review](decisions.md#100-review-work-item-9-2026-08-15)

### AD-29 — The MCP server is hand-rolled, synchronous and newline-framed

- **Binds:** `okf mcp`
- **Prevents:** losing every response when a client closes stdin — the measured failure of
  the official SDK's stdio server — and tripling a 4 MB binary to adapt four methods.
- **Rule:** Four methods (`initialize`, `tools/list`, `tools/call`, `ping`) over
  newline-delimited JSON-RPC 2.0, using `System.Text.Json` on the AOT-clean path. One line
  is read, handled, written and flushed before the next is read, so nothing is ever in
  flight at EOF and response order is request order. Protocol structures are emitted
  compact because a raw newline splits a message; payloads are emitted indented because a
  model has to read them. Nothing but JSON-RPC reaches stdout.
- **Source:** [mcp milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-mcp-milestone-2026-08-14)

### AD-30 — MCP is read-only, namespaced, and cannot be widened per call

- **Binds:** `okf mcp`, `OkfBundle.TryResolve`, `OkfConceptReader`
- **Prevents:** a client escaping the scope the server was launched with, and a write tool
  that turns a knowledge base into a surface an untrusted host can edit.
- **Rule:** Three tools — `okf_list`, `okf_search`, `okf_read` — namespaced because a
  client mixes servers in one flat list. No write tool exists; stamping and index
  generation stay CLI operations. No tool takes a *scope* path — a tool's `path` argument
  is bundle-relative and nothing else; the scope is fixed by `okf mcp [path]` and
  re-resolved per call, so a client cannot widen what the server was launched with.
  Containment is two layers: a path grammar that refuses absolute paths, `~`, `..` however
  spelled, a backslash and a NUL, and one shared primitive (`OkfBundle.TryResolve`) that
  resolves against the bundle root and refuses anything that leaves it, symlinks
  followed. Errors split by
  who can recover: a malformed argument or a traversal attempt is a JSON-RPC `-32602`; a
  miss the model can act on is a tool result with `isError: true`.
- **Source:** PRD MCP-4, MCP-5,
  [mcp milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-mcp-milestone-2026-08-14)

### AD-31 — Configuration precedence is fixed, and `OKF_HOME` is not a severity layer

- **Binds:** `okf lint`, `okf verify`, `okf.json`, CLI flags — the commands that read
  configuration; every other verb takes none
- **Prevents:** an environment variable quietly changing what a team's committed contract
  says a rule means.
- **Rule:** Built-in defaults → global file (`XDG_CONFIG_HOME`, else `~/.config/okf/okf.json`)
  → project file (`<vault>/okf.json`) → CLI arguments. A layer that omits a key leaves it
  to the layer below. `OKF_HOME` moves *which* personal vault is read, never a rule's
  severity. An explicit `--config` file replaces the project config rather than adding to
  it. `--verbose` reports the effective value and the layer that set it.
- **Source:** [lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14),
  superseding [Q4](decisions.md#open-question-resolutions-2026-08-14), which listed
  `OKF_HOME` as a precedence layer and omitted the built-in defaults

### AD-32 — `okf.json` is JSONC at the vault root, committed as the team contract

- **Binds:** `okf/okf.json`, `okf/custodian/recipe.json`, `okf init`
- **Prevents:** a severity promotion nobody can review, because the reason lives outside
  the file that made it.
- **Rule:** The project config is `<vault>/okf.json` — the vault is what discovery already
  resolved, so a command that found the bundles has found the config. It is parsed with
  comments and trailing commas allowed, and that is stated in the header comment of the
  `okf.json` that `okf init` writes, in the bundle's vaults-and-config concept, and here.
  `recipe.json` follows the same convention. The accepted
  cost is that a strict JSON schema or editor will flag a file the tool reads happily.
- **Source:** [lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14),
  [`okf init`](decisions.md#proposed-decisions-decided-2026-08-15-review-9-okf-init-work-item-4-2026-08-15)

### AD-33 — A distribution ships `bundles/` and keeps the vault's layout

- **Binds:** `okf bundle`, the release job
- **Prevents:** shipping producer-side machinery to a consumer, and breaking a
  cross-bundle link that would otherwise have resolved.
- **Rule:** The walk starts at `bundles/<name>/` and carries concepts, reserved files and
  non-markdown content alike — §6.3 makes code content. Everything outside a bundle root
  (`raw/`, `custodian/`, `okf.json`, the vault README) is never reached rather than
  skipped. Inside a bundle root the walk is AD-49's and the bundler adds one rule of its
  own: a short list of junk *suffixes* (`*~`, `*.swp`, `*.orig`, …) is excluded, and a file
  whose name does not say it is junk gets packaged — including a dot-prefixed concept,
  because shipping a tree that differs from the one `okf lint` judged is the failure this
  prevents. The distribution keeps `bundles/<name>/`, so a link between two
  packaged bundles still resolves. Formats are `tar.gz` (default), `zip` and `dir`, which
  between them cover every shape §3 permits.
- **Source:** [what ships](decisions.md#what-ships-and-what-never-does),
  [smaller calls](decisions.md#smaller-calls-recorded-because-they-will-be-asked-about)

### AD-34 — A cross-bundle link that leaves the distribution stays dangling and is recorded

- **Binds:** `okf bundle`, `okf-bundle.json`
- **Prevents:** vendoring a concept along with its trust state, provenance and
  `stale_after` — a copy that cannot be re-verified and will diverge silently.
- **Rule:** §6.1 makes a dangling link a legal artifact of the format, so the bundler
  neither vendors the target nor refuses to package. Every link whose resolved target is
  not in the shipped file set is listed in `externalLinks` and warned about once per link
  on stderr. Danglingness is decided by asking whether the target is in the file set, never
  by guessing from the link's shape.
- **Source:** [the spike, resolved](decisions.md#the-cross-bundle-reference-spike-resolved-leave-the-link-dangling-and-say-so)

### AD-35 — `okf-bundle.json` sits at the distribution root and claims integrity, not authenticity

- **Binds:** `okf bundle`, `okf bundle --verify`, `latest.json`
- **Prevents:** a manifest that quietly withdraws the §1 promise, and a digest being read
  as a signature.
- **Rule:** The manifest describes the distribution, so it sits outside every bundle root,
  where `okf.json` sits: a consumer who deletes it still holds a complete `cat`-readable
  bundle. Keys are camelCase (only the spelling is okf-net's; the `okfVersion` *value* is
  the spec's `"0.2"`). It is the one unhashed file. `sourceVault` is a name, never a path.
  `generator` is a §7 actor carrying the semantic version without build metadata. Hashes
  detect corruption, truncation and half-applied edits — never forgery; signing is
  deferred and unclaimed.
- **Source:** [the attestation](decisions.md#okf-bundlejson-the-attestation-and-where-it-sits),
  [self-hosted install](decisions.md#proposed-decisions-decided-2026-08-15-review-9-self-hosted-install-work-item-25-2026-08-15)

### AD-36 — Archives are byte-reproducible, with exactly one clock reading

- **Binds:** `okf bundle`, the tag pipeline
- **Prevents:** a release artifact whose bytes change on every rebuild, which cannot be
  checked against a published digest.
- **Rule:** Entries sorted by path ordinally, the manifest sorting with them; no directory
  entries; every timestamp the constant `1980-01-01T00:00:00Z`; ownership `0:0` with empty
  names and mode `0644` on every file. The tar entry format is **GNU** — PAX embeds the
  writing process id, ustar throws on a path over 100 characters — and GNU `atime`/`ctime`
  are left unset, because pinning them puts data where ustar's `prefix` field begins and
  breaks CPython's `tarfile`. `generatedAt` is the only clock reading in the packaging
  path and `--generated-at` pins it; the tag pipeline passes `$CI_COMMIT_TIMESTAMP`.
  Reproducibility and interoperability are two claims: the archive was read back by a
  second implementation (CPython's `tarfile`) when the format was chosen, and the standing
  test is that the whole 155-byte ustar `prefix` window stays NUL.
- **Source:** [deterministic archives](decisions.md#deterministic-archives-and-the-one-clock-reading),
  [lessons.md](lessons.md)

### AD-37 — Verification never extracts, and an unlisted entry is a finding

- **Binds:** `okf bundle --verify`, `okf bundle --lint`
- **Prevents:** a planted symlink verifying clean and then writing into somebody else's
  filesystem on extraction.
- **Rule:** `--verify` streams each entry and hashes it in memory; nothing is ever
  extracted, so a hostile `../../x`, an absolute path or a symlink entry is *reported*, not
  written. Findings are *missing*, *modified*, *unlisted* and *unreadable*; a link or
  device entry is *unlisted*, not skipped — directory entries alone are skipped, because
  the bundler writes none. An archive is identified by magic bytes,
  not by its name. A distribution with no manifest is *unreadable*, never a pass. `--lint`
  lints what shipped as a stranger sees it — default severities, no `okf.json`, no vault —
  by materializing the same plan through the same writer, never an archive's own paths.
- **Source:** [`--verify` and `--lint`](decisions.md#--verify-and---lint)

### AD-38 — Bundle content is untrusted data on every rendered page

- **Binds:** `okf site`, `OkfSiteMarkdown`, `OkfSiteHtml`, `site.js`
- **Prevents:** a `<script>` smuggled through a concept body running from a `file://` page
  with no origin to contain it.
- **Rule:** Raw HTML in a body is escaped, never emitted (`DisableHtml`). A link or image
  destination carries an allowlisted scheme (`http`, `https`, `mailto`) or none at all,
  read on the AST the way a browser reads it — case-insensitively, with ASCII whitespace
  and C0 controls dropped first — and anything else is percent-escaped into one inert
  relative segment and marked `broken`, because §6.1 says mark rather than drop; an
  autolink, which carries no separate label to mark, is replaced by its own text. Every
  page is well-formed XML and the suite parses it; embedded JSON uses the default encoder
  so it can neither close its own `<script>` element nor break the parse. Untrusted values
  are escaped where they are written and read back with `textContent` — a tag into the href
  and into the label — and the one `innerHTML` write on the page takes generator-escaped
  article HTML and nothing else.
- **Source:** [site milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-static-site-milestone-work-item-6-2026-08-15)

### AD-39 — The site ships no third-party JavaScript and loads nothing at run time

- **Binds:** `okf site`, `src/Okf.Core/Assets/`
- **Prevents:** a supply-chain artifact nobody here can rebuild, and a page that reports
  the reader's browsing to a CDN.
- **Rule:** No third-party JavaScript, vendored or CDN-loaded — the graph is a
  Fruchterman-Reingold layout on a `<canvas>` in about 390 lines of our own. Markdown is
  the opposite call: Markdig renders bodies **server-side**, so pages are readable with
  scripting off. The stylesheet and client script stay editable files under
  `src/Okf.Core/Assets/`, embedded as manifest resources — data in the image, not a type
  the trimmer must be told to keep. Every link is relative, so the site works identically
  from GitLab Pages and from `file://`.
- **Source:** [site milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-static-site-milestone-work-item-6-2026-08-15)

### AD-40 — Shape says what can be clicked, and the site never writes into what it renders

- **Binds:** `okf site`, `site.css`, `--out`
- **Prevents:** a reader pressing a trust badge that does nothing, and the next run reading
  its own HTML as a bundle.
- **Rule:** The pill (`.chip`) is the site's one shape for "press me" — worn by tags, the
  bundle drill-down and the removable active filters, and by nothing that only states a
  fact. Everything informational is `.facts` / `.signal`: a real definition list with no
  fill, border, hover or pointer. Colour is a status channel and never the only one — every
  status also carries a glyph and a word. Generation is deterministic — injected date,
  ordinal page order, byte-identical reruns — and carries no layout coordinate at all; the
  graph is laid out in the reader's browser from a seeded PRNG under an iteration bound, so
  it is reproducible per reader rather than per build. `--out` pointing into a bundle, or
  at a bundle's parent, is refused with exit 2 before anything is written, and nothing is
  ever deleted from the output directory.
- **Source:** [site milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-static-site-milestone-work-item-6-2026-08-15)

### AD-41 — Version stamping is the SDK's own flow, and an unstamped build says so

- **Binds:** `Directory.Build.props`, `mise run publish-aot`, the `publish` job, `okf version`
- **Prevents:** a laptop build and a release candidate being indistinguishable, and a
  `git describe` trailer sorting above the tag it followed.
- **Rule:** `Directory.Build.props` sets a default and performs no arithmetic; the flow is
  `-p:Version` plus `-p:SourceRevisionId`. The unstamped default is `0.0.0-dev`, never
  `1.0.0`. No MSBuild target shells out to git — `mise run publish-aot` derives a local
  version from `git describe` and normalizes it, dropping the distance trailer and carrying
  the commit as build metadata. Two version strings ship deliberately: a tagged build (the
  `publish` job, rc or stable) prints the bare version from `okf version` — the tag already
  identifies the commit — while a local, untagged build (`mise run publish-aot`, a plain
  `dotnet build`) still carries `+<sha>`, because nothing else says where it came from.
  `okf version --verbose` prints a second line, `commit: <sha>` (or `commit: unknown` when
  unstamped), on every build regardless of whether the sha made it into the first line.
  MCP's `serverInfo.version` prints the bare semantic version unchanged, because semver §10
  excludes build metadata from precedence.
- **Source:** [version stamping](decisions.md#proposed-decisions-decided-2026-08-15-review-9-version-stamping-and-tag-pipelines-work-item-10-2026-08-14),
  [bare release versions](decisions.md#proposed-decisions-bare-release-versions-work-item-53-2026-08-16)

### AD-42 — Every hop of the release chain re-verifies the bytes, and the host pulls

- **Binds:** the `publish` job, `latest.json`, `install.sh`, `install.ps1`, and the artifact
  host's `sync.sh` (which lives on the host, not in this repository)
- **Prevents:** a package whose name lies about its contents, and an SSH credential on a
  shared runner that can write to the box serving the install script.
- **Rule:** *(updated 2026-08-15 after #36, which made the release multi-platform, and
  again after #41, which added the skills archive; the single-binary wording this replaces
  was true when written.)* One tag pipeline publishes **eight** assets under one package
  version: three binaries (`okf-linux-x64`, `okf-osx-arm64`, `okf-win-x64.exe`), the
  knowledge bundle (`okf-net-knowledge.tar.gz`), the agent skills
  (`okf-skills.tar.gz`, a deterministic tar of `skills/*/SKILL.md` that no installer
  fetches, because every binary embeds the same files), `latest.json`, and both
  installers. CI stamps from the tag,
  never from `git describe`, and the job compares `okf version` from the freshly compiled
  binary against the bare `<tag minus the leading v>` — a tagged build carries no `+<sha>`,
  the tag already identifies the commit (issue #53) — and separately checks that
  `okf version --verbose` names the commit on its second line, before uploading anything: a
  runnable check for `linux-x64` only, because a Linux runner cannot execute the other two,
  which get a grepped `file` assertion instead so a mislabelled image cannot reach a tester.
  `latest.json` is the release contract, a map keyed by asset name, and carries both a
  relative `path` (resolved
  against the installer's base URL) and the absolute registry `url`, because the installer
  must not know about GitLab and the host's `sync.sh` needs a URL it can pull with a token.
  Nothing pushes into the artifact host: by design it holds a read-only registry token and
  re-verifies each asset on the way in, publishing version directories by rename — a claim
  about the host, which this repository cannot check. Each installer
  downloads to a temp directory, compares digests, prints both on a mismatch, and only then
  stages and renames within the install directory — `~/.local/bin/okf` for `install.sh`,
  `%LOCALAPPDATA%\okf\bin\okf.exe` for `install.ps1`, neither needing root or
  Administrator. `install.sh` follows an `https` base URL only to `https`, on the first hop
  and every redirect after it; `install.ps1` cannot make that promise — PowerShell has no
  `--proto-redir` — so it requires an `https` base URL and says so in the file.
- **Source:** [version stamping](decisions.md#proposed-decisions-decided-2026-08-15-review-9-version-stamping-and-tag-pipelines-work-item-10-2026-08-14),
  [self-hosted install](decisions.md#proposed-decisions-decided-2026-08-15-review-9-self-hosted-install-work-item-25-2026-08-15)

### AD-43 — Dependencies must be Apache-2.0-compatible, and CI proves it

- **Binds:** every `PackageReference` in `Okf.sln`, every pinned dotnet tool
- **Prevents:** a copyleft or source-available license entering a shipped or test binary,
  and a package that changed license between majors slipping in on a remembered fact.
- **Rule:** Only permissive licenses (MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, MS-PL).
  Check the license of the **exact version** being added. The `licenses` job builds a
  CycloneDX SBOM of `Okf.sln` and fails on any component not covered by
  `scripts/licenses-allowed.json`; an SPDX expression must be wholly satisfiable and a
  component with no SPDX id fails unless `scripts/license-overrides.json` pins that exact
  name and version with an evidence link. Two things the SBOM cannot see are checked by
  hand instead, and the finding recorded in the commit: a pinned dotnet tool, because a
  tool manifest is not a project reference; and anything under `spikes/`, which is outside
  `Okf.sln` on purpose and must therefore never ship.
- **Source:** [AGENTS.md](../AGENTS.md), [MS-PL ruling](decisions.md#ruling-ms-pl-is-allowlisted-2026-08-14),
  [mutation testing](decisions.md#proposed-decisions-decided-2026-08-15-review-9-mutation-testing-work-item-11-2026-08-14)

### AD-44 — Tests must be shown to constrain the code

- **Binds:** every test in the repository
- **Prevents:** a vacuous assertion shipping as coverage — two did, and only adversarial
  review caught them.
- **Rule:** A test added alongside new behaviour is demonstrated to FAIL without that
  behaviour, **or** derives its expected values from an independent oracle (the Python
  reference implementation, the spec text). State which in the commit. An assertion must
  not be satisfiable vacuously. `mise run mutate` is the mechanical version of that check:
  Stryker in solution mode, one score, no exclusions, thresholds `break` 60 / `low` 70 /
  `high` 85 — scheduled and blocking, manual and non-blocking on a branch push, never a
  per-push gate, because a gate that flakes gets disabled.
- **Source:** [AGENTS.md](../AGENTS.md),
  [mutation testing](decisions.md#proposed-decisions-decided-2026-08-15-review-9-mutation-testing-work-item-11-2026-08-14)

### AD-45 — A `sources[].resource` naming an in-bundle concept is written bundle-root-relative

- **Binds:** both producer skills, every concept's `sources`, `OKF0308`
- **Prevents:** a citation that only resolves because okf-net is lenient — the consumer who
  implements §6.2 literally gets a dangling pointer.
- **Rule:** When a source is a concept in the same bundle, write it with the leading slash:
  `/references/agent-skills.md`, never `references/agent-skills.md`. §6.2 resolves a
  relative path **from the citing document**, so the bare form names
  `<citing-dir>/references/…` and survives here only because `LintText` falls back to the
  bundle root — a leniency added for Google's own bundles, not a form to author in. The
  reader stays tolerant of both; the writer emits one.
- **Source:** [custodian activation](decisions.md#proposed-decisions-decided-2026-08-15-review-9-custodian-activation-on-this-repo-work-item-20-2026-08-15),
  [lint/search friction](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-lintsearch-friction-milestone-work-item-21-2026-08-14)

### AD-46 — The pre-commit hook carries only what is instant; `okf lint` is a CI gate

- **Binds:** `.githooks/`, the `dogfood` job, ACC-6
- **Prevents:** every commit in every consuming project waiting on a `dotnet run`, and the
  hook being deleted the week after it is added.
- **Rule:** No git hook invokes the `okf` binary. `pre-commit` runs `markdownlint-cli2` over
  staged markdown, and `check-manifest.py` only when something under `okf/raw/` is staged;
  `commit-msg` runs `scripts/check-commit-msg.sh`. `okf lint`, `okf index --check` and the
  manifest check all run in CI. ACC-6 asks that the path *can* run in a hook, not that it
  must.
- **Source:** [custodian activation](decisions.md#proposed-decisions-decided-2026-08-15-review-9-custodian-activation-on-this-repo-work-item-20-2026-08-15),
  PRD ACC-6

### AD-47 — `about.md` describes its directory, and a conventional filename never collides

- **Binds:** `OkfIndexGenerator`, `OkfBundle.IsConventionalFile`, `OKF0303`
- **Prevents:** the one file the index generator asks a bundle to add being reported as a
  near-duplicate of every sibling directory's copy of it.
- **Rule:** A subdirectory entry takes its blurb from `<subdir>/about.md`'s `description`,
  and is emitted blurb-less when there is none. A conventional filename is exempt from
  **both** arms of `OKF0303` — the filename arm and the title arm — and neither reports a
  collision nor seeds one for a later file, because the generic title that comes with a
  conventional name (`title: About`) is as much a convention as the name. Reserved names
  need no exemption: §3.1 keeps them out of the concept walk entirely.
- **Source:** [Q1](decisions.md#open-question-resolutions-2026-08-14),
  [lint review flags](decisions.md#proposed-decisions-decided-2026-08-15-review-9-lint-review-flags-2026-08-14),
  [index milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-index-milestone-2026-08-14)

### AD-48 — Tag governance ships off, and its two rules move together

- **Binds:** `OKF0304`, `OKF0305`, `lint.tagRegistry`
- **Prevents:** promoting the registry rule alone, which would make deleting the `tags` key
  the cheapest way to satisfy tag governance.
- **Rule:** `OKF0304` (missing-tags) and `OKF0305` (unregistered-tag) both ship `hidden` and
  are one decision: a consumer raises both or neither. A registry is opt-in configuration —
  absent `lint.tagRegistry`, `OKF0305` has nothing to say. Where the registry is enabled it
  is a ratchet rather than a redesign: `warning`, so a new word is named rather than
  rejected, and the trigger for promoting it to `error` is recorded rather than left to
  inertia.
- **Source:** [custodian activation](decisions.md#proposed-decisions-decided-2026-08-15-review-9-custodian-activation-on-this-repo-work-item-20-2026-08-15),
  [lint milestone](decisions.md#proposed-decisions-decided-2026-08-15-review-9-the-okf-lint-milestone-2026-08-14)

### AD-49 — One walk decides what is bundle content, and a dot-prefix hides nothing

- **Binds:** `OkfBundle.MarkdownFiles`, `OkfBundle.ContentFiles`, `OkfDiscovery`, and every
  surface that reads them — lint, index, search, MCP, the site, the bundler
- **Prevents:** `okf lint` reporting conformance over a tree it did not read, and two
  surfaces disagreeing about which files a bundle contains.
- **Rule:** `MarkdownFiles()` and `ContentFiles()` are the only walk; nothing enumerates a
  bundle for itself. Spec §11 conforms every non-reserved `.md` file in the tree, so a
  leading dot is not an exemption: `.hidden.md` is linted, indexed, searched, rendered and
  packaged, and a dot-directory is descended. The one exception is
  `OkfBundle.IgnoredMetadataNames` — `.DS_Store`, `.git`, `.hg`, `.idea`, `.obsidian`,
  `.svn`, `.vscode`, `Thumbs.db`, `desktop.ini` — matched on the whole name,
  case-insensitively, for files and directories alike, used by the walk and by bundle
  discovery, and asserted literally by a test so it cannot grow unreviewed. A symlinked
  directory is still never descended, dot-prefixed or not.
- **Source:** [dot-prefixed markdown](decisions.md#proposed-decisions-dot-prefixed-markdown-is-linted-work-item-42-2026-08-15)

### AD-50 — The skills ship inside the binary, and a committed pointer never names a home directory

- **Binds:** `skills/*/SKILL.md`, `OkfSkills`, `OkfSkillInstaller`, `okf skills`, `okf init`,
  both installers
- **Prevents:** an installed `okf` whose custodian recipe points at files the machine does
  not have — the whole of #41 — and a second download standing between a fresh install and
  a working one.
- **Rule:** `Okf.Core.csproj` globs `../../skills/*/SKILL.md` into `EmbeddedResource`, so a
  skill added to the repository is embedded by the next build, and a test asserts the
  embedded set equals the on-disk set name for name and byte for byte. `okf skills install`
  writes them — never a network call (AD-7) — to `$XDG_DATA_HOME/okf/skills`
  (`%LOCALAPPDATA%\okf\skills` on Windows) plus each host directory that already exists,
  resolving every path through `OkfEnvironment`. A file whose bytes differ is
  `skipped (modified)` and the run exits 0 unless `--force`, the same never-overwrite
  discipline as `okf init`. `okf init` resolves each recipe pointer against the **project**
  only — `skills/<name>/SKILL.md`, then a project-scoped host install — and otherwise writes
  the §5.1 descriptor `okf skills path <name>` — the command that resolves it — because an
  absolute or `~` path in a committed
  file resolves on one machine. Both installers run `okf skills install` after the version
  check, non-interactively, and treat its failure as a warning naming the command to re-run.
- **Source:** [shipping the skills](decisions.md#proposed-decisions-shipping-the-skills-work-item-41-2026-08-15)

### AD-51 — The registry is the only way scope widens, and widening it is always explicit

- **Binds:** `OkfRegistry`, `OkfScope`, `okf register`, `okf unregister`, `okf registry`,
  `okf search --scope`, `okf mcp --scope`, `search.scope`
- **Prevents:** a query that returns different results on two machines because one of them
  had something extra lying around — and a tool that put it there without being asked.
- **Rule:** `registry.json` lives beside the global config
  (`$XDG_CONFIG_HOME/okf/`, else `~/.config/okf/`) and is **strict JSON**, not the JSONC
  `okf.json` is (AD-32), because its only writer is `okf register`. It is written
  deterministically (entries sorted by id, one trailing newline) and atomically (temp file,
  then rename); reading one never writes one. An entry is `id` + absolute `path` + `kind`
  (`vault`|`bundle`) + canonical `registeredAt`, where the **id is a slug of the directory
  name chosen once at register time and never recomputed**, so an entry survives its
  directory moving; a path is classified exactly as `OkfDiscovery` classifies one.
  `okf register` and `okf unregister` are idempotent and spend exit 0 on the no-op; nothing
  auto-registers, and `autoRegister` is accepted in the **global layer only**, recorded and
  validated, with no behaviour attached. Dead entries are *reported* by `okf registry list`
  and removed only by the explicit `okf registry prune` — no lint rule writes. Scope is
  `project` (the default, byte-for-byte AD-31's CLI-1 resolution) | `personal` (resolvable
  from `OKF_HOME`/`~/okf` without the registry) | `registered` | `all`, set by `search.scope`
  through AD-31's chain and by `--scope` above it, and resolved for the CLI and for
  `okf mcp` by one Core entry point (`OkfScope.Resolve`). A registered path that has gone
  missing is a note on stderr, never an error; bundles are de-duplicated by the path the
  filesystem ends at, because AD-26's collection statistics span the whole resolved corpus.
  MCP scope is fixed at launch and no tool argument grew (AD-30): a bundle is named by a
  `label` that is unique in scope.
- **Source:** [the vault registry and scope](decisions.md#proposed-decisions-the-vault-registry-and-scope-work-item-43-2026-08-16)

### AD-52 — Bookkeeping is written by a verb, spliced rather than re-serialized, and never repaired

- **Binds:** `okf capture add`, `okf capture close`, `okf generated stamp`,
  `OkfCaptureWriter`, `OkfStamp`, both producer skills
- **Prevents:** an agent hand-writing the structure layer — a `sha256` it typed, a
  timestamp it guessed, a JSON edit it improvised — into the one record whose whole job is
  to prove the artifact did not change.
- **Rule:** The fields the Design Paradigm calls machine-owned have a command that writes
  them and no other way in. Three exist and they are CLI-only; MCP stays read-only
  (AD-30). Every edit is a **text splice** located by `Utf8JsonReader` token offsets, so
  only the appended, replaced or closed region moves and every other byte — whitespace,
  key order, the timestamp spellings already on disk — survives as found; the spliced
  result is then read back through `OkfCaptureManifest` and required to hold the entry
  just written, exactly as AD-23 requires of a stamped concept. The file is replaced by
  temp-and-rename. Refusals never repair (AD-18): an ingested entry is immutable (exit 1),
  an uningested one is replaced in place, a manifest that does not read as one is reported
  and left alone (exit 2). The entry id is **derived** from the item's own name so it
  cannot disagree with the `<id>.<ext>` / `<id>/` join `check-manifest.py` enforces, and
  `--form` is an assertion checked against the item's shape rather than a label. `--by` is
  required with no configured default — AD-22's chain resolves a *person* and a capture is
  usually an agent's — and `--captured-at` / `--at` accept only the canonical instant
  (AD-24).
- **Source:** [deterministic write bookkeeping](decisions.md#proposed-decisions-deterministic-write-bookkeeping-work-item-44-2026-08-16)

### AD-53 — `okf upgrade` is the one network path, and it never widens past its own directory

- **Binds:** `OkfUpgrade`, `OkfUpgradeManifest`, `OkfUpgradeVersion`, `UpgradeCommand`,
  `latest.json`, and AD-7's exception
- **Prevents:** a toolset that phones home on a verb nobody pointed at a network, and a
  self-replacing binary that leaves a half-written file where an executable was.
- **Rule:** All of okf-net's network access is `okf upgrade`, and all of it lives in
  `OkfUpgrade.cs` behind an injectable fetch delegate — no other file references
  `System.Net.Http`, no other verb reaches it, and okf never checks for an update it was
  not asked to check for. Only an `https` base URL is accepted, or `http` to **loopback**;
  redirects are followed by hand, five hops, and one that leaves `https` is refused, because
  a digest fetched down the same cleartext channel as the bytes it describes proves nothing.
  The manifest is `latest.json` at `<base>/` or `<base>/v<version>/`, read relative to
  `OKF_INSTALL_URL` — the same variable both installers read, and never the registry `url`
  the same manifest carries for the host's `sync.sh` (AD-42). A manifest is read from an
  untrusted host, so nothing it says is taken on trust: a `path` that climbs out of the base
  URL is refused rather than fetched, and both reads are bounded — 1 MB for the manifest,
  200 MB for an asset — because the staging file is written into the directory the user's
  binary lives in and an endless body would otherwise fill it. Integrity, not authenticity:
  the `sha256` is AD-19's, the manifest is unsigned, and that is AD-35's and AD-42's
  deferral unchanged. The download is staged **inside the running binary's own directory**
  (a rename is atomic only within a filesystem), verified there, and only then renamed into
  place; POSIX overwrites in one move, Windows renames the loaded image to `okf.exe.old`
  first and `okf upgrade` alone deletes that file, at the start of its own install path.
  Nothing outside that directory is read or written, and any refusal — an unusable base URL,
  an unreadable manifest, no asset for this platform, a digest mismatch printing both
  digests, an oversized or out-of-base asset, a target not named `okf`, a directory that
  cannot be written — happens before a byte moves and is exit 2. `--check` is severity-blind and mechanical in AD-5's sense: 0
  current, 1 available, no download.
- **Source:** [`okf upgrade`](decisions.md#proposed-decisions-okf-upgrade-work-item-23-2026-08-16),
  [self-hosted install](decisions.md#proposed-decisions-decided-2026-08-15-review-9-self-hosted-install-work-item-25-2026-08-15)

## Consistency Conventions

| Concern | Convention |
| --- | --- |
| Type names | Every public type in `Okf.Core` is `Okf`-prefixed. The **value type keeps the plain domain noun** and a **static helper is named for what it does** — `OkfCanonicalTimestamp` (helper) beside `OkfLifecycleInstant` (value). Names say what they do; a name that carries its own meaning is the documentation. |
| C# style as the code stands | File-scoped namespaces everywhere (62 of 62 files under `src/`). Private instance fields are plain `camelCase`, disambiguated with `this.` — no underscore prefix appears anywhere in `src/`. `var` is used freely (666 declaration sites). `Nullable` and `TreatWarningsAsErrors` are on, set per-csproj; there is no `.editorconfig`. Ringo's own idiom, and the `_camelCase`-versus-`camelCase` choice, is enforced by configuration in [#31](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/31) — until then this row records what is true, not what is wanted. |
| Comments | For what a name cannot carry — a constraint, a spec section, a non-obvious why. Never narration of the next line. Reviewers flag narrative comments as noise. |
| Commits | Conventional Commits v1.0.0 on every non-merge commit, enforced by the `commit-msg` hook and re-checked in CI across the whole push range. Commit *type* drives the release, so a type must be honest — no tooling change smuggled into a `docs:` commit. |
| Diagnostics | An `OKF####` identifier is the only thing accepted as a configuration key; a kebab nickname is a label, never a key, though `--json` emits it beside the id for readability. A diagnostic carries id, severity, message, path, bundle root, and a line where locating one is cheap. There is no column. |
| Timestamps | Written: RFC 3339 UTC `Z`, second precision, through `OkfCanonicalTimestamp`. Read: tolerant, compared at the coarser precision, on the date **as written**. |
| Actors | SPEC §7: `<producer>/<version>` (`okf/1.0.0-rc.24`, `claude-fable/5`), `human:<id>`, `process:<id>`. Validated before it is written. |
| Machine-maintained JSON | `okf.json` and `recipe.json` are **JSONC** — comments and trailing commas — because the reason for a promotion is the half a reviewer needs. `raw/manifest.json`, `okf-bundle.json` and `latest.json` are strict JSON with **camelCase** keys; okf reads the first two with `JsonDocument` rather than a deserializer, and `latest.json` is deliberately shallow enough that `install.sh` parses it in POSIX shell without one. |
| Markdown | `markdownlint-cli2`, `MD013` off repo-wide; the vault adds `MD025: false` via an `extends` line, because a nested config replaces the parent rather than merging. `mise run lint` is the gate. |
| File layout | `src/Okf.Core` (all logic) · `src/Okf.Cli` (verbs, args, JSON writers, MCP) · `tests/Okf.Core.Tests`, `tests/Okf.Cli.Tests`, `tests/install-sh` · `skills/<name>/SKILL.md` · `okf/` (the dogfood vault) · `docs/` (this file, `decisions.md`, `prd.md`, `lessons.md`, and `spikes/`) · `scripts/`, `.githooks/`, `.config/` (the dotnet tool manifest), `spikes/` (outside `Okf.sln`), and `install.sh` and `install.ps1` at the root. |
| Skills | One directory per skill, frontmatter of `name` and `description` only, body plain markdown. Nothing host-specific: no tool names, no `allowed-tools`, no slash commands. Every step ends on a checkable, environment-verified completion criterion. |

## Stack

Verified on this checkout. Versions are what the repository pins today; the code owns this
row set once it exists.

| Name | Version | License | Role |
| --- | --- | --- | --- |
| .NET target framework | net10.0 | — | `Okf.Core` and `Okf.Cli` |
| dotnet SDK (mise-pinned) | 10 | — | `mise.toml`; CI image `mcr.microsoft.com/dotnet/sdk:10.0` |
| YamlDotNet | 18.1.0 | MIT | Frontmatter, through the representation model and emitter only (AD-9) |
| Markdig | 1.3.2 | BSD-2-Clause | Server-side markdown rendering for `okf site` (AD-39) |
| xunit | 2.9.3 | Apache-2.0 | Test framework |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Test runner |
| Xunit.SkippableFact | 1.5.61 | MS-PL | Conditional tests; test-only, never shipped ([ruling](decisions.md#ruling-ms-pl-is-allowlisted-2026-08-14)) |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test host |
| coverlet.collector | 6.0.4 | MIT | Coverage collection |
| dotnet-stryker | 4.16.0 | Apache-2.0 | Mutation testing; local tool, invisible to the SBOM (AD-43, AD-44) |
| cyclonedx | 6.2.0 | Apache-2.0 | SBOM generation for the license gate; local tool, so its own license is checked by hand (AD-43) |
| markdownlint-cli2 | latest (mise-managed) | — | `mise run lint` |
| semantic-release | 25.0.9 + 3 plugins + the `conventionalcommits` preset, all pinned exactly in the `release` job | — | Cuts `vX.Y.Z` from `main` and `vX.Y.Z-rc.N` from `dev`, and creates the GitLab Release |
| GitLab (self-hosted) | CE 19.0.1 | — | Repository, CI, Pages, generic package registry |
| CI images | node:22-slim · mcr.microsoft.com/dotnet/sdk:10.0 · python:3.12-slim | — | Default · `.dotnet` template · `test-install` |
| Artifact host | nginx behind caddy-tycho, serving `/opt/stacks/okf-artifacts/www` | — | `get.okf.tychostation.dev` (internal only) |

## Structural Seed

### Container view

```mermaid
flowchart TB
    ci["GitLab CI<br/>validate · deploy · release"] --> cli
    skills["Agent skills<br/>capture · custodian · vault"] --> cli
    hosts["MCP hosts<br/>Claude Code, Cursor"] --> mcp
    subgraph binary["okf — one NativeAOT binary"]
        cli["Okf.Cli<br/>verbs, args, JSON writers"]
        mcp["MCP server<br/>okf mcp"]
        core["Okf.Core<br/>parse · lint · index · search<br/>trust · bundle · site"]
    end
    cli --> core
    mcp --> core
    core --> vault[("vault<br/>bundles · raw · custodian")]
    core --> dist["distribution<br/>tar.gz · zip · dir"]
    core --> site["static site<br/>GitLab Pages · file://"]
```

### Lint pipeline

```mermaid
flowchart TB
    hook["pre-commit hook"] --> mdl["markdownlint-cli2<br/>staged .md only"]
    hook --> cm["check-manifest.py<br/>only when okf/raw/ is staged"]
    job["CI dogfood job"] --> lint["okf lint okf/"]
    job --> chk["okf index --check"]
    job --> cm
    lint --> walk["OkfLinter<br/>walk bundle roots<br/>+ one vault pass for OKF0310"]
    walk --> sev["OkfSeverityResolver<br/>defaults · global · project · args"]
    sev --> diag["diagnostics<br/>id · severity · path · line"]
    chk --> gen["OkfIndexGenerator<br/>drifted or orphaned"]
    diag --> code["exit 0 · 1 · 2"]
    gen --> code
```

### Capture, ingest, cite

```mermaid
flowchart TB
    src["a source that cannot<br/>defend itself"] --> raw["okf/raw/ITEM.html"]
    raw --> entry["raw/manifest.json entry<br/>sha256 · capturedAt · ingestion null"]
    entry --> ingest["custodian ingests"]
    ingest --> ref["references/ITEM.md<br/>an ordinary concept"]
    ingest --> closed["ingestion closed<br/>at · by · concepts"]
    ref --> cite["concept cites it<br/>sources id + footnote in prose"]
    closed --> rule["OKF0310 watches the sha256"]
```

### Release

```mermaid
flowchart TB
    merge["merge to dev"] --> rel["release job<br/>semantic-release"]
    promote["promote dev to main<br/>explicit, when Ringo ships"] --> rel
    rel --> tag["tag vX.Y.Z-rc.N from dev<br/>vX.Y.Z from main<br/>+ GitLab Release"]
    tag --> pub["publish job<br/>tag pipeline only"]
    pub --> build["publish 3 RIDs<br/>linux-x64 AOT · osx-arm64<br/>win-x64 · stamped from the tag"]
    build --> gate{"okf version matches tag,<br/>--verbose names the commit?"}
    gate -- no --> stop["fail · upload nothing"]
    gate -- yes --> reg["generic package registry<br/>okf/VERSION · 8 assets<br/>3 binaries · knowledge bundle<br/>skills archive"]
    reg --> man["latest.json<br/>per asset: path · size · sha256 · url"]
    man --> sync["sync.sh on the host<br/>pull · re-verify · rename"]
    sync --> host["get.okf.tychostation.dev"]
    host --> inst["install.sh (Linux · macOS)<br/>install.ps1 (Windows)<br/>read latest.json · sha256 · atomic mv"]
```

### Search and MCP request path

```mermaid
sequenceDiagram
    participant H as MCP host
    participant S as okf mcp
    participant C as Okf.Core
    participant B as bundles
    H->>S: tools/call okf_search
    S->>S: resolve scope,<br/>per call
    S->>C: OkfSearchEngine.Search
    C->>B: read concepts,<br/>skip reserved files
    B-->>C: frontmatter<br/>and body
    C-->>S: results: score, tier,<br/>stale, snippet
    S-->>H: SearchJson — the same<br/>bytes as okf search
```

### Site generation

```mermaid
flowchart TB
    vault["resolved vault"] --> build["OkfSiteBuilder<br/>builds OkfSiteModel"]
    build --> mdown["OkfSiteMarkdown<br/>Markdig · DisableHtml<br/>scheme allowlist"]
    assets["Assets/site.css + site.js<br/>embedded resources"] --> gen
    mdown --> gen["OkfSiteGenerator"]
    gen --> multi["one page per markdown file<br/>+ landing · dashboard · graph"]
    gen --> one["--single-file<br/>one fragment-routed page"]
    multi --> pages["pages job<br/>default branch only"]
    multi --> local["file:// · no network"]
```

## Capability → Architecture Map

| Capability | Lives in | Governed by | PRD |
| --- | --- | --- | --- |
| `okf lint` | `OkfLinter`, `OkfRules`, `OkfSeverityResolver`, `LintText`, `MarkdownScanner`; `LintCommand` renders | AD-3, AD-4, AD-5, AD-11, AD-12, AD-31, AD-49 | CORE-3, CORE-4, CORE-8, CLI-5, CLI-6, CLI-7, CLI-15, ACC-1 |
| `okf index` | `OkfIndexGenerator`, `OkfIndex`; `IndexCommand` renders | AD-5, AD-13, AD-14, AD-15, AD-49 | CORE-9, CORE-10, CLI-9, CLI-10, ACC-3, ACC-7 |
| `okf search` | `OkfSearchEngine`, `OkfSearchQuery`, `OkfTokenizer`, `OkfScope`; `SearchCommand` + `SearchJson` render | AD-6, AD-7, AD-26, AD-27, AD-28, AD-49, AD-51 | CORE-11, CLI-3, CLI-11 |
| `okf mcp` | `McpServer`, `McpToolset`, `McpCommand` over `OkfSearchEngine`, `OkfConceptReader`, `OkfIndexGenerator`, `OkfScope` | AD-6, AD-28, AD-29, AD-30, AD-49, AD-51 | MCP-1 … MCP-5, CORE-12 |
| `okf inbox` / `okf verify` | `OkfInboxScanner`, `OkfLifecycleInstant`, `OkfStamp`, `OkfVerifyIdentity` | AD-20, AD-21, AD-22, AD-23, AD-24, AD-25, AD-31 | CORE-14, CORE-15, CLI-12, CLI-13 |
| `okf capture` / `okf generated` | `OkfCaptureWriter`, `OkfCaptureManifest`, `OkfStamp`; `CaptureCommand` + `GeneratedCommand` render | AD-16, AD-18, AD-19, AD-21, AD-23, AD-24, AD-30, AD-52 | SKILL-3, ACC-5 |
| `okf init` | `OkfScaffold`, `OkfDiscovery`, `OkfIndexGenerator` (it *writes* `okf.json`, never reads one) | AD-2, AD-13, AD-15, AD-32 | CLI-8 |
| `okf bundle` | `OkfBundler`, `OkfBundle`, `OkfDistribution*`, `OkfCaptureManifest.Sha256Of` | AD-19, AD-33, AD-34, AD-35, AD-36, AD-37, AD-49 | PRD §5 (post-MVP roadmap), CLI-14 |
| `okf site` | `OkfSiteBuilder`, `OkfSiteModel`, `OkfSiteMarkdown`, `OkfSiteHtml`, `OkfSiteGenerator`, `Assets/` | AD-27, AD-38, AD-39, AD-40, AD-49 | PRD §5 (post-MVP roadmap) |
| Skills | `skills/okf-capture`, `skills/okf-custodian`, `skills/okf-vault` — prose that calls the CLI, embedded in the binary | AD-1, AD-6, AD-16, AD-18, AD-20, AD-50, AD-52 | SKILL-1 … SKILL-8 |
| `okf skills` | `OkfSkills`, `OkfSkillInstaller`; `SkillsCommand` + `SkillsArguments` render | AD-6, AD-7, AD-50 | SKILL-1 … SKILL-8, CLI-14 |
| Custodian | `okf/custodian/` (`recipe.json`, `check-manifest.py`), the two producer skills, the scheduled `custodian-inbox` job | AD-17, AD-18, AD-21, AD-32, AD-52 | SKILL-7, ACC-5, ACC-6 |
| Install and release | `.releaserc.yml`, the `publish` job, `latest.json`, `install.sh`, `install.ps1`, `tests/install-sh/` | AD-19, AD-41, AD-42, AD-43, AD-50 | CLI-17, Q10 |
| `okf upgrade` | `OkfUpgrade` (the only network path), `OkfUpgradeManifest`, `OkfUpgradeVersion`, `OkfUpgradePlan`; `UpgradeCommand` + `UpgradeArguments` render | AD-5, AD-6, AD-7, AD-9, AD-19, AD-41, AD-42, AD-53 | CLI-14, CLI-17 |
| Vault resolution and config | `OkfDiscovery`, `OkfWorkingSet`, `OkfConfig`, `OkfEnvironment` | AD-2, AD-31, AD-32 | CORE-13, CLI-1, CLI-4 |
| Registry and scope | `OkfRegistry`, `OkfScope`; `RegistryCommand`, `RegistryArguments`, `ScopeSettings` render and layer | AD-7, AD-26, AD-30, AD-31, AD-51 | CLI-2, CLI-3, MCP-3 |

## Deferred

The board's post-1.0 and polish columns, plus the dimensions this spine deliberately does
not fix. Board:
[ringo/okf-net](https://gitlab.tychostation.dev/ringo/okf-net/-/issues).

| Item | Deferred because |
| --- | --- |
| [#16](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/16) — OTEL instrumentation | Blocked on a design discussion with Ringo: scope across CLI and MCP, exporter choice, AOT compatibility, and the opt-in privacy posture. |
| [#17](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/17) — edit and verify from the published site | Needs a decision about who may verify and how a merge-request-based `okf verify` flow surfaces; the site is deliberately view-only today (AD-40). |
| [#22](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/22) — optional components | Keeping the CLI one small file while heavy runtimes are fetched on demand and checksum-verified; the shape only matters once something heavy exists (#24). |
| [#24](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/24) — vector search layer | The spike says GO-with-conditions, but sqlite-vec makes the binary three files and the NuGet package is a stale third-party alpha — AD-8's single-file claim is what it costs. |
| [#26](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/26) — security posture for public exposure | An unauthenticated host serving a script people pipe into `sh` is a different security object; it needs auth, rate limiting, abuse handling and a signed manifest, none of which exist (AD-35, AD-42). |
| [#29](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/29) — mermaid diagrams on the static site | AD-39 forbids third-party JavaScript, so rendering has to happen CI-side as SVG; that is a new build step, not a generator change. |
| [#32](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/32) — `externalBundles` obtain-from hint | Additive to `okf-bundle.json` and does not change AD-34's dangling-and-record answer; a versioned manifest addition. |
| [#33](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/33) — remote MCP bridge for claude.ai | A non-stdio transport is one of the things AD-29 named as the trigger to revisit the hand-rolled loop; it also depends on #26. |
| [#34](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/34) — advisory on excessive prose in `index.md` | A new hygiene diagnostic with a configurable threshold; the range is fixed (AD-11) but the threshold's default needs real bundles to calibrate against. |
| [#35](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/35) — spike: what okf-net should learn from BMAD's skills | A talk-first study of how a workflow can make a document's *shape* deterministic while its prose is not — this spine is the first instance. What it changes about `skills/` is the spike's output, not an input. |
| [#13](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/13) — dead-code and contextual DRY pass | Waits for the feature milestones to stop moving; the mutation run's 137 no-coverage mutants are its input list. |
| [#14](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/14) — composed-method style | A mechanical refactor across public surfaces, deliberately separate from any logic change. |
| [#15](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/15) — SOLID and DI audit, boundary isolation | Formalizes AD-9's boundary rule; a DI container is itself an AOT question, so it is a considered change rather than a cleanup. |
| [#18](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/18) — site UX polish | Client-side search, provenance panel, staleness badges, a `log.md` timeline; none changes an invariant. |
| [#31](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/31) — C# style enforced by configuration | Ringo has not yet picked between `_camelCase` and `camelCase` private fields; until he does, the Consistency Conventions row records what the code shows. |
| Signing | Hashes are integrity, not authenticity. A signature needs a key, a distribution channel for the public half, and a rotation policy — none of which exists. `okf-bundle.json`'s shape leaves room for a detached signature (AD-35), and `okf upgrade` inherits the same gap (AD-53). |
| A second release channel for `okf upgrade` | The two-channel split (stable from `main`, rc from `dev`) is decided; the host publishes one manifest at its root, so `--channel rc` is parsed, documented as reserved, and reads the same file rather than requesting a URL nothing serves (AD-53). |
| An on-disk search index | Every search walks the resolved bundles and reads them; four reference bundles is milliseconds. When it stops being, the generated artifact is #24's, and a bundle must stay complete without it. |
| Phrase queries, negation, field-qualified free text | Each is a new grammar to freeze in the JSON contract (AD-28), and none is needed by the capture skill's search-before-create loop. |
| Q8's near-duplicate heuristic, Q9's CI-commit signal | `OKF0303` ships a normalized title-or-filename collision and keeps its id when #24 replaces the heuristic. The "human actor on a CI commit" warning has no reliable signal decided, so it has no rule id. |
| An extractor (`okf bundle --extract`) | Reading an archive is needed for `--verify` and writing one for packaging; unpacking is `tar -xzf`'s job and a consumer already has it. |
| Branch review environments on Pages | Built, tried against the real instance, and removed: `pages.path_prefix` and `pages.expire_in` are Premium/Ultimate keywords silently ignored on GitLab CE 19.0.1, so a feature branch published over the production site. Reviewers run `mise run site` locally. |
| NativeAOT beyond `linux-x64`, and the targets nobody has asked for | *(updated 2026-08-15 after #36.)* Three RIDs ship: `linux-x64` is NativeAOT, `osx-arm64` and `win-x64` are trim-safe self-contained, because NativeAOT compiles through the host's toolchain and the only runner here is Linux (AD-8). Still deferred: NativeAOT for those two, which needs a macOS runner ([#38](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/38)) and a Windows runner ([#39](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/39)) and buys back ~9 MB and the cold start and nothing else; and `osx-x64`, musl and `linux-arm64`, each one line in the publish job and one case label in `install.sh`, not built on speculation. `latest.json`'s asset map carries the release's eight assets and has room for more. |

Several milestone items whose work has landed (#2 – #7) are still open on the board; they
are execution bookkeeping, not deferrals.
