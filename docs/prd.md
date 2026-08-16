# okf-net — Product Requirements

> **Status:** MVP requirements, reconciled against the code on 2026-08-15 (work item #37);
> CLI-2 and CLI-3's registry scope landed in work item #43 (2026-08-16).
> Everything in §2 is **BUILT**; the three post-MVP items in §5
> marked **BUILT** shipped too. A `BUILT` marker names the work item that delivered the
> requirement and is a pointer, not a rewrite — where a resolution has since been
> superseded, the original text stays and the correction sits beside it.
>
> Rationale for every decision referenced here lives in
> [decisions.md](decisions.md); this document states *what must be built and how it is
> verified*, not *why*. Where the two disagree, decisions.md wins and this document is
> wrong. [architecture.md](architecture.md) is the invariant spine between them, and its
> Capability → Architecture Map names the types each requirement lives in.
>
> Format under implementation: OKF v0.2, specified in
> `~/code/knowledge-catalog/okf/SPEC.md` (referred to below as "the spec", cited by
> section, e.g. §11).

## 1. Overview

okf-net is a .NET toolset for producing, validating, and consuming **OKF v0.2 knowledge
bundles**: directory trees of markdown files with YAML frontmatter. It ships as a
library (`Okf.Core`), a single CLI binary (`okf`) that also hosts an MCP server
(`okf mcp`), and three agent skills (two producers and one consumer — §2.4).

The format is the interop layer. Nothing okf-net produces requires okf-net to consume —
a bundle stays `cat`-readable and `git clone`-portable (spec §1).

### 1.1 Users

- **Solo knowledge-keeper (primary).** Keeps a personal vault at `~/okf/`, captures
  knowledge as it is learned, and wants it retrievable months later without remembering
  where it was filed.
- **Dev teams with project bundles.** Commit an `okf/` vault beside their code; the
  bundle is reviewed like code, maintained by a custodian agent through git hooks and
  CI, and behaves identically on every machine and in CI.
- **Consuming agents, including non-human ones.** Reach bundles through progressive
  disclosure (index files), the CLI, or MCP. Consumption is read-only and never requires
  executing bundle-supplied code.
- **Foreign-bundle consumers.** Point okf-net at a bundle it did not produce (e.g.
  Google's published bundles) and get lint, index synthesis, and search with no
  in-bundle cooperation.

### 1.2 Goals

1. Make OKF v0.2 conformance (§11) mechanically checkable in a hook or CI job, in one
   process invocation, with no server and no network.
2. Make any bundle — ours or foreign — navigable and searchable by an agent that can
   only run a command.
3. Make the trust, freshness, and provenance families (§5) actionable: derivable,
   reportable, and stampable.
4. Keep the toolset out of the bundle: bundles are consume-only artifacts, the toolset is
   referenced by version and never vendored per project (decisions §1).
5. Ship as a self-contained binary so the implementation language never leaks to
   consumers (decisions §3).

### 1.3 Non-goals for MVP

The following were explicitly **out of scope** for MVP and tracked as roadmap in §5. Three
have since been built post-MVP and are marked **BUILT** here with the work item that
delivered them; the list is kept rather than pruned, because what was deliberately left out
of MVP is part of the record.

- **Bundler** — packaging a bundle for consume-only distribution. **BUILT (2026-08-15, work
  item #5)** as `okf bundle` — see §5.
- **Static site generator** — rendering a bundle as a browsable site. **BUILT (2026-08-15,
  work item #6)** as `okf site`, published to GitLab Pages by the `pages` job — see §5.
- **Pi extension / widget** — the TypeScript shim and any UI surface. Still out of scope;
  nothing has been built.
- **Vectorization / semantic index** — sqlite-vec or equivalent. The **spike** ran (work
  item #8, `docs/spikes/2026-08-15-vectorization.md`) and returned GO-with-conditions; the
  layer itself is deferred to
  [#24](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/24) and no vector index
  ships.
- **Custodian staleness-refresh loop** — automated re-derivation of expired concepts and
  the acknowledgment workflow around it. **BUILT (2026-08-15, work item #7)**, and built to
  a deliberately narrower shape than "automated": the derivation and surfacing ship
  (`okf inbox`, `okf verify`, the scheduled `custodian-inbox` job), and the fetch-and-draft
  step is a documented procedure in `skills/okf-custodian/SKILL.md` that a person starts.
  Nothing refreshes itself and no job opens a merge request — see §5.

Also out of scope, permanently or until a concrete need appears: executing executors or
attesters (§10 is *recorded and surfaced*, never run by okf-net), defining a type
taxonomy, and any storage or serving layer beyond the filesystem.

## 2. MVP functional requirements

Requirements are testable statements with acceptance criteria. IDs are stable; future
work references them.

Build order is fixed by decisions §"MVP build order": Core parse/validate → `okf index` →
`okf search` → `okf mcp` → skills.

**Status of this section: every requirement below is BUILT.** CLI-2, the registry, was the
last one and landed in work item #43. The per-section notes give the work item
that delivered each group; `docs/architecture.md`'s Capability → Architecture Map names the
types each one lives in.

### 2.1 Okf.Core

`Okf.Core` holds **all** logic. The CLI and MCP server are thin adapters over it
(decisions §4). Every requirement below is satisfied by the library and is unit-testable
without a process boundary.

**BUILT (CORE-1 … CORE-15).** The public API surface was frozen by the 1.0.0 review (work
item #9) on 2026-08-15, and the two changes that review ordered landed in work item #30.

- **CORE-1 — Frontmatter parse.** Parse a UTF-8 markdown file into `(frontmatter, body)`
  per spec §4.
  - A file whose first line is not `---` parses as empty frontmatter with the entire text
    as body (no error).
  - An opened but unterminated `---` block is an error.
  - Frontmatter that is valid YAML but not a mapping is an error.
  - Invalid YAML is an error carrying the underlying parser message.
  - A single leading newline between the closing `---` and the body is consumed.
- **CORE-2 — Unknown-key preservation.** Round-tripping a document preserves every
  frontmatter key, including ones the library does not model (spec §4.1: consumers SHOULD
  preserve unknown keys and MUST NOT reject unrecognized fields).
  - Parse → serialize of any concept in the four reference bundles (§4) preserves all
    keys and their insertion order; keys are not alphabetized.
  - Producer extensions (e.g. acme_retail's `not:`) survive a round trip unchanged.
  - Scalars are not re-typed by the round trip (dates, quoted version strings such as
    `okf_version: "0.2"`, and numeric-looking strings keep their original form).
- **CORE-3 — Conformance validation (§11).** Report whether a bundle tree is conformant.
  - Every non-reserved `.md` file has a parseable frontmatter block → else non-conformant.
  - Every such frontmatter has a non-empty `type` → else non-conformant.
  - Reserved files (`index.md`, `log.md`) follow §8/§9 structure when present → else
    non-conformant.
  - Nothing else makes a bundle non-conformant. Missing optional fields, unknown `type`
    values, unknown keys, broken cross-links, and missing `index.md` files MUST NOT be
    reported as conformance failures (§11).
  - Non-`.md` files in the tree are ignored (the reference bundles carry `viz.html` and
    `.py` attesters and remain conformant).
- **CORE-4 — Reserved-file structure checks.** Validate `index.md` against §8 and `log.md`
  against §9.
  - `index.md`: no frontmatter, except a bundle-root `index.md` MAY carry `okf_version`
    (§12); body is one or more `#` sections of `* [Title](link) - description` bullets.
  - `log.md`: `##` date headings in ISO `YYYY-MM-DD` form, newest first; entry prose is
    unconstrained (the leading bold word is convention, not requirement).
- **CORE-5 — `verified` normalization (§5.2).** A bare `{ by, at }` mapping is treated as
  a one-element list.
  - Absent key → empty list; bare mapping → one element; list → itself with non-mapping
    elements dropped; any other YAML shape → empty list.
- **CORE-6 — Trust-tier derivation (§5.3).** Derive `unverified` | `machine-confirmed` |
  `human-reviewed` from the normalized `verified` list.
  - No verification events → `unverified`.
  - Any event whose `by` starts with `human:` → `human-reviewed`.
  - Otherwise → `machine-confirmed`.
  - Derivation reads only `verified`; `generated`, `status`, and `sources` never affect
    the tier.
- **CORE-7 — Staleness (§5.5).** A concept is stale when `today >= stale_after`.
  - Absent or empty `stale_after` → not stale.
  - A YAML-native date and an ISO `YYYY-MM-DD` string behave identically.
  - A datetime value is compared on its date part.
  - An unparseable value → not stale (never an error).
  - The comparison date is injectable, so staleness is deterministic in tests.
- **CORE-8 — Source-drift signal.** Report, per concept, whether any `sources[].last_modified`
  is later than `generated.at` (decisions §5's compensating control for cited-live
  sources).
  - Concepts lacking `generated.at` or lacking source `last_modified` values produce no
    signal.
- **CORE-9 — Index generation (§8).** Generate `index.md` for every directory in a bundle
  that contains indexable content.
  - Entries are grouped under `#` headings and rendered as
    `* [Title](relative-link) - description`.
  - Title falls back to the filename stem when `title` is absent; the description is the
    concept's frontmatter `description` and is omitted (with its separator) when absent.
  - Subdirectories are listed as entries linking to the subdirectory's own index, with the
    description taken from that subdirectory's `about.md` frontmatter `description` when
    present; when `about.md` is absent, the entry is emitted with no blurb (Q1, resolved).
  - `index.md` and `log.md` are never listed as entries.
  - Generation is deterministic: identical input tree → byte-identical output, with no
    network or model call.
  - For bundles we produce, the bundle-root `index.md` carries `okf_version: "0.2"`
    frontmatter (§12, decisions §2), and its first entry links the bundle's
    `about-this-bundle.md` concept.
  - Regeneration is idempotent and preserves nothing hand-written — generated index files
    are outputs, and drift is a lint concern (CLI-9).
- **CORE-10 — On-the-fly index synthesis.** For a bundle (or directory) with no
  `index.md`, synthesize the equivalent listing in memory without writing to disk
  (spec §8: consumers MAY synthesize).
  - Synthesis uses the same renderer as CORE-9, so a synthesized listing and a generated
    file are identical for the same tree.
  - Synthesis never writes into a bundle the tool does not own.
- **CORE-11 — Search.** Search a bundle tree and return ranked concept matches (Q7,
  resolved).
  - Matches over frontmatter (`title`, `description`, `tags`, `type`) and body text, by
    **token**, not substring: text is lowercased and split on non-alphanumerics, with no
    stemming in v1.
  - Only concepts are corpus entries. Reserved files (`index.md`, `log.md`) are excluded,
    and `raw/` is outside every bundle root and therefore never reached.
  - Ranking is BM25-style over field-weighted text — title ×3, `tags`/`type` ×2,
    `description` ×2, body ×1 — and fully deterministic: no clock, no network, no model
    call, ties broken by bundle then path.
  - Query terms are AND-ed; when the AND set is empty the search falls back to OR and says
    which mode produced the results.
  - Each result carries: concept ID (path minus `.md`, spec §2), bundle-relative path,
    absolute path, bundle, title, type, description, score, trust tier, stale flag, and a
    bounded match snippet — never the full body.
  - Filterable by `type` and `tags`; filters restrict candidates before scoring.
  - Results are returned as data, not formatted text, so the CLI and MCP render the same
    result set differently.
- **CORE-12 — Concept read.** Load a single concept by ID, returning frontmatter, body,
  derived trust tier, and stale flag.
  - IDs are validated and confined to the bundle root; `..` traversal and absolute paths
    are rejected.
- **CORE-13 — Vault and bundle discovery.** Given a starting directory, discover the
  project vault, the bundle roots inside it, and registered vaults. **BUILT** —
  registered vaults joined the other two in work item #43 (`OkfRegistry`, `OkfScope`).
  - Walk up from the start directory for a directory named `okf/`; the nearest one wins.
  - `OKF_HOME` overrides the personal-vault location; the default personal vault is
    `~/okf/` (visible, not hidden).
  - A bundle root is a directory under `<vault>/bundles/`; a directory pointed at
    directly (a foreign bundle) is also a valid bundle root.
  - Discovery is pure: it reads the filesystem and configuration and performs no writes —
    reading the registry included; only `okf register`, `okf unregister` and
    `okf registry prune` write it.
- **CORE-14 — Stamping.** Provide the two stamp operations, writing only the fields
  decisions §7 permits.
  - Generation stamping writes `generated.{by,at}` and nothing else.
  - Verification stamping appends `{ by, at }` to `verified`, normalizing a pre-existing
    bare mapping into a list first.
  - Stamping preserves all other keys, key order, and the body byte-for-byte.
  - An actor value is validated against the §7 convention (`<producer>/<version>`,
    `human:<id>`, `process:<id>`).
- **CORE-15 — Acknowledgment state.** Derive whether a concept is unacknowledged:
  `generated.at` is newer than the latest `verified[].at`, **or** `status: draft`
  (decisions §7). No new frontmatter field is introduced.

### 2.2 `okf` CLI

The CLI is a thin wrapper over `Okf.Core` (decisions §4). It must be usable from a git
hook: single process, no daemon, no network, meaningful exit code. (`okf upgrade` is the
one verb that uses a network, and nothing puts it in a hook — CLI-16.)

**BUILT.** The shipped verbs are `okf init` (work item #4), `okf lint`,
`okf index`, `okf search` (work item #1), `okf inbox` and `okf verify` (work item #7),
`okf bundle` (work item #5), `okf site` (work item #6), `okf mcp` (work item #2),
`okf skills` (work item #41), `okf capture` and `okf generated` (work item #44),
`okf register` / `okf unregister` / `okf registry` (work item #43), plus
`okf help` and `okf version`. §3's table is the full surface, row for row.

- **CLI-1 — Vault resolution.** Every command resolves its working set the same way.
  - Default target is the project vault found by walking up for `okf/`.
  - With no project vault and no explicit path, the command targets the personal vault
    (`OKF_HOME`, else `~/okf/`).
  - An explicit path argument overrides discovery and may point at any bundle root,
    including a foreign one.
  - Resolution is reported in `--verbose` output so "which bundle did it read" is never a
    guess.
- **CLI-2 — Registry. BUILT (work item #43, `docs/architecture.md` AD-51).**
  `$XDG_CONFIG_HOME/okf/registry.json` (else `~/.config/okf/registry.json`) holds the
  registry of known vaults and bundles — strict JSON, written deterministically and
  atomically, and read by nothing that writes.
  - `okf register [path]` adds an entry; it is idempotent (re-registering the same path is
    a no-op success). With no path it targets the vault CLI-1 resolves.
  - `okf unregister [path|id]` removes an entry; removing an unknown entry is a no-op
    success.
  - An entry carries `id`, absolute `path`, `kind` (`vault`|`bundle`) and `registeredAt`.
    The id is a slug of the directory name chosen once at register time and never
    recomputed, so an entry survives its directory being moved.
  - `okf registry list [--json]` reports every entry and whether its path still exists;
    `okf registry prune` removes the entries whose paths are gone. No lint rule writes.
  - The personal vault is an ordinary registry entry with no special-casing
    (decisions §6). `--scope personal` still resolves it from `OKF_HOME`/`~/okf` without
    a registry entry, because discovery has always known where it is.
  - Auto-registration is **off** by default and is not implemented; `autoRegister` is
    accepted, validated and recorded in the **global** config only.
- **CLI-3 — Search scope. BUILT (work item #43).** Search defaults to project-only.
  - With a project vault resolved, only that vault's bundles are searched — identical
    results on every machine and in CI.
  - Registry entries (including the personal vault) are included only when configuration
    or an explicit flag opts them in.
  - The four scopes are `project` (default), `personal`, `registered` and `all`, spelled
    the same as the `--scope` flag and as the `search.scope` setting. `okf mcp` takes the
    same flag, at launch only (MCP-3, AD-30).
  - A registered path that no longer exists is reported once on stderr and skipped; it is
    never an error.
- **CLI-4 — Configuration precedence.** CLI args > `OKF_HOME` environment variable >
  project config > global config at `~/.config/okf/okf.json` (decisions §6, Q4 resolved).
  **As shipped the chain is built-in defaults → global file (`XDG_CONFIG_HOME`, else
  `~/.config/okf/okf.json`) → project file (`<vault>/okf.json`) → CLI arguments, and
  `OKF_HOME` is not in it** — it selects the personal vault, never a rule's severity (Q4's
  pointer below; `docs/architecture.md` AD-31).
  - A setting present at a higher layer wins; lower layers still supply unset keys.
  - The project config is committed and is the team contract; the global config is
    per-machine.
  - `--verbose` reports the effective value and its source layer for any setting that
    changed behavior.
- **CLI-5 — Lint default severity.** `okf lint` errors **only** on spec §11 conformance.
  - A bundle that is conformant but carries every warning in CLI-7 exits 0 by default.
  - A bundle that violates §11 exits non-zero regardless of configuration.
  - Principle: defaults block only what the spec says; every additional block is consumer
    configuration (decisions §7).
- **CLI-6 — Roslyn-style severity configuration.** okf-net adopts Roslyn's four severities
  — hidden, info, warning, error — for every diagnostic (Q5, resolved); any diagnostic is
  reconfigurable to any of the four, per rule, with no exemptions.
  - Per-rule severity is set in configuration and overridable by CLI flag, honoring
    CLI-4 precedence.
  - `treatAllWarningsAsErrors` promotes every diagnostic currently at **warning** severity
    to error. Broken internal link defaults to **info**, not warning, so this setting does
    not touch it by itself — consumer configuration may still promote it explicitly, same
    as any rule.
  - An unknown rule identifier in configuration is itself reported (a typo must not
    silently disable a rule).
- **CLI-7 — Warning set.** `okf lint` implements exactly this warning set (decisions §7);
  each is an independently addressable rule.

  | Warning | Fires when | Default |
  | --- | --- | --- |
  | Citation integrity | A body footnote label has no matching `sources[].id`, or a `sources[].id` is never cited by a footnote *reference* (a definition alone is the note, not a claim attributed to the source) | warning |
  | Staleness | `today >= stale_after` | warning, never blocks by default |
  | Source drift | A `sources[].last_modified` is newer than `generated.at` (CORE-8) | warning, never blocks by default |
  | Broken internal link | A bundle-internal markdown link resolves to no file (§6.1: consumers MUST tolerate) | info by default; promotable like any diagnostic (Q5, resolved) |
  | Near-duplicate concept | Two concepts are judged near-identical (see Q8) | warning |
  | Missing `description` | A concept has no `description` (§4.1 recommends it; index entries degrade without it) | warning |
  | Missing `tags` | A concept has no `tags` | off (opt-in) |
  | Unregistered tag | A tag is absent from the bundle's tag registry (beyond-spec extension) | off (opt-in) |
  | Self-verification | Any `verified[].by` equals `generated.by` | warning |
  | Human actor on CI commit | `generated.by` is a `human:` actor on a CI-authored commit | **not shipped** — no rule id, no implementation (Q9) |
  | Missing source resource | A `sources[]` entry carries no `resource`, which §5.1 requires within an entry | warning |
  | Unresolvable source resource | A `sources[].resource` written as a path names nothing inside the bundle (§6.2); absolute URLs and §5.1 scope descriptors are never checked | info |
  | Link leaves the bundle | A markdown link resolves outside the bundle root (§6.2), which was previously unreported and so indistinguishable from a correct link | info |
  | Raw item mutated | An ingested `raw/` item no longer matches the `sha256` its capture-manifest entry records (CLI-9) | warning |

  The three rows above the last were added from the dogfood friction log (work item #21,
  from #19's note_168); see decisions.md, "the lint/search friction milestone". The last
  is CLI-9's `raw/`-immutability rule, which landed with `okf init` (work item #4).

  **BUILT, one row excepted.** Every row above except *Human actor on CI commit* ships as a
  numbered rule. The shipped catalog is `OKF0001`–`OKF0004` (conformance),
  `OKF0101`–`OKF0103` (provenance), `OKF0201`–`OKF0202` (trust) and `OKF0301`–`OKF0310`
  (hygiene). `okf lint --list-rules` prints every id with its **default** severity;
  `--verbose` on a real run reports the **effective** one and the layer that set it, and
  `docs/architecture.md` AD-11 fixes the ranges. The two "off (opt-in)" rows above ship as
  `hidden` and move together as one decision (AD-48).

- **CLI-8 — `okf init` scaffolding.** `okf init` creates the project layout from
  decisions §2.
  - Creates `okf/README.md` (repo-facing, deliberately outside every bundle root),
    `okf/bundles/<name>/`, `okf/custodian/`, and `okf/raw/` (the drop zone for captured
    artifacts, sibling of `bundles/`; Q3, resolved).
  - The new bundle gets a generated root `index.md` with `okf_version: "0.2"` and an
    `about-this-bundle.md` concept naming the toolset, the custodian, the update cadence,
    and the consumption options (plain reading / MCP).
  - Writes a project config that promotes to **error**: mutation, after ingestion, of a
    `raw/` item tracked in the capture manifest, and hand-edit drift in generated files.
  - Refuses to overwrite existing files; re-running on an initialized project is a
    reported no-op.
- **CLI-9 — Generated-drift and `raw/` ingestion-immutability rules.** The two rules
  `okf init` promotes must exist as rules.
  - Generated drift: an on-disk generated file differs from what `okf index` would emit
    for the same tree.
  - `raw/` mutation: a `raw/` item recorded as ingested in the capture manifest changed
    after its capture. **Detection is by the manifest's recorded `sha256`, not by git** —
    the hash is the format-level record, it works in a vault that is not a work tree, and
    it needs no process launched from a library that is offline and AOT-clean by contract
    (CLI-16). The scope is the *vault*, the only rule for which that is true, because
    `raw/` sits outside every bundle root (Q3); a bundle with no vault around it leaves
    the rule inapplicable rather than failing. `references/` carries no okf-net-specific semantics
    (Q3, resolved) — files under it are ordinary spec §6.3 concepts, validated the same as
    any other concept (CORE-1, CORE-3), and this rule does not apply to them.
- **CLI-10 — `okf index`.** Write generated index files for a bundle.
  - `--check` writes nothing and exits non-zero when any index would change (the CI/hook
    form of CLI-9).
  - Output is byte-stable across runs and across machines.
- **CLI-11 — `okf search`.** Query resolved bundles from the command line.
  - Supports `--type`, `--tag` (both repeatable, and equivalent to the inline `type:`/
    `tag:` filter syntax) and `--limit` (default 10).
  - Resolves its working set exactly as `okf lint` does (CLI-1), so the two commands never
    disagree about which bundles they are looking at.
  - Human-readable default output is links-first — rank, score, path, title, type, trust
    and stale markers, snippet — and never prints a full concept body.
  - `--json` emits the full CORE-11 result records, including trust tier and stale flag, as
    a stable sorted array. That array is the contract `okf mcp`'s `search` tool returns
    (MCP-2, MCP-3).
  - Exits 0 with an empty result set (no matches is not an error).
- **CLI-12 — `okf inbox`.** List unacknowledged concepts (CORE-15) across the resolved
  scope.
  - Each row shows concept ID, why it is unacknowledged (regenerated since verification /
    draft), trust tier, and stale flag.
  - Ordering is deterministic. `--json` is supported.
- **CLI-13 — `okf verify`.** Stamp human verification on one or more concepts.
  - Writes `verified: { by: human:<id>, at: <now> }` via CORE-14, where `<id>` is
    `verify.actor` from okf config, falling back to `git config --global user.email` when
    unset (Q6, resolved). The fallback reads the **global** git config only, never
    repo-local.
  - Refuses to stamp, with an actionable message, only when neither `verify.actor` nor the
    global git email is available.
  - After stamping, the concept no longer appears in `okf inbox` unless it is `draft`.
  - Never edits the body and never touches `generated`.
- **CLI-14 — Exit codes.** Exit codes are part of the contract because hooks and CI branch
  on them.
  - `0` — success; no diagnostics at error severity.
  - `1` — diagnostics at error severity (lint failures, `--check` drift).
  - `2` — usage or environment failure (bad arguments, unknown rule id, unresolvable
    vault, unreadable file).
  - Warnings alone never change the exit code unless promoted (CLI-6).
- **CLI-15 — Machine-readable diagnostics.** Every lint diagnostic carries a stable rule
  identifier, severity, file path, and line/column where determinable, and is emitable as
  JSON.
  - Human output is stable enough to grep; JSON output is the contract for CI
    annotations.
  - Rule identifiers are `OKF####` numeric codes with reserved category ranges —
    conformance `00xx`, provenance `01xx`, trust `02xx`, hygiene `03xx` — per Q2
    (resolved). Docs may also give kebab nicknames; nicknames are not valid configuration
    keys.
- **CLI-16 — Offline and hermetic.** No command invokes a model, and no command that reads
  or writes knowledge makes a network call.
  - The full MVP surface runs with networking disabled.
  - **Amended 2026-08-16 (work item #23).** `okf upgrade` is the single exception and the
    only network path in the toolset: it replaces the binary with a release it verifies
    against the published manifest. It is confined to one library file, reachable from no
    other verb, and never runs unless it is the verb invoked — no startup check, no
    background poll (`docs/architecture.md` AD-7, AD-53).
- **CLI-17 — Distribution. BUILT (2026-08-15, work items #25 and #36), except the NuGet
  package.** Each tag pipeline publishes seven assets under one package version: three
  binaries — `okf-linux-x64` (NativeAOT), `okf-osx-arm64` and `okf-win-x64.exe` (trim-safe
  self-contained, because NativeAOT compiles through the host toolchain and the only runner
  is Linux) — this bundle as `okf-net-knowledge.tar.gz`, the `latest.json` release manifest
  naming each asset's relative path, size and `sha256`, and the two installers that read it,
  `install.sh` (Linux and macOS) and `install.ps1` (Windows). `Okf.Core` is **not** published
  to a NuGet registry (still open, Q10). The CLI ships as a self-contained, single-file binary
  installable without a .NET SDK (decisions §3), targeting `net10.0` (Q10, partially
  resolved).
  - NativeAOT is preferred; a trim-safe self-contained non-AOT publish is the documented
    fallback if AOT proves incompatible with a dependency.
  - `Okf.Core` additionally publishes as a NuGet package to the self-hosted GitLab
    instance's built-in NuGet registry (instance configuration to be verified at first
    publish).
  - A release produces binaries for the supported targets and a `curl | sh` install path.
  - (Specific RIDs, and whether AOT survives the still-undecided YAML serialization
    library, remain open — see Q10.)

### 2.3 `okf mcp`

**BUILT (2026-08-15, work item #2).** Three namespaced tools — `okf_list`, `okf_search`,
`okf_read` — over a hand-rolled, newline-framed JSON-RPC 2.0 loop.

- **MCP-1 — Subcommand-hosted server.** `okf mcp` starts an MCP server over stdio from the
  same binary (kcmd precedent, decisions §4).
  - No separate install, package, or port; the server is an adapter over `Okf.Core`, not a
    second implementation.
- **MCP-2 — Tool surface.** The server exposes at minimum `search`, `read`, and `list`
  over the resolved vaults.
  - `search` mirrors CORE-11 including `type`/`tag` filters and returns structured
    results.
  - `read` returns one concept's frontmatter, body, trust tier, and stale flag (CORE-12).
  - `list` returns a directory listing, using a generated index when present and a
    synthesized one otherwise (CORE-10), so progressive disclosure works on foreign
    bundles.
- **MCP-3 — Scope parity.** The server resolves vaults and applies search scope by the
  same rules as the CLI (CLI-1, CLI-3, CLI-4).
  - Identical query, identical working directory, identical results between `okf search`
    and the MCP `search` tool.
- **MCP-4 — Read-only.** MVP exposes no write tool. Stamping and index generation stay CLI
  operations.
- **MCP-5 — Containment.** Every path returned or accepted is confined to a resolved
  bundle root; traversal outside it is rejected.

### 2.4 Skills

**BUILT (2026-08-15, work items #3 and #27), and three shipped rather than two.** The two
producer skills SKILL-1 … SKILL-8 specify are `skills/okf-capture` and
`skills/okf-custodian` (work item #3). A third, consumer-side skill — `skills/okf-vault`,
which walks an agent from a question to the concepts that answer it and back to a citation
carrying the trust tier — was added by work item #27 and carries no numbered requirement
here; `skills/README.md` says which of the three fires when.

Skills ship in `skills/` with releases (decisions repo layout). They are prose
instructions for agents; they call the CLI rather than reimplementing anything.

- **SKILL-1 — Capture skill exists.** `skills/` contains a capture skill that turns
  something just learned into a concept in the right bundle.
- **SKILL-2 — Search before create.** The capture skill searches the resolved bundles
  before writing, and either extends the matching concept or states why a new one is
  warranted.
- **SKILL-3 — Capture vs cite.** The skill applies the decisions §5 test — *if this source
  changed or vanished tomorrow, could the custodian still re-verify the concept?*
  - Yes → cite via `sources[].resource` with `last_modified` and a version pin where
    available.
  - No → capture the artifact into `raw/` (the drop zone, outside every bundle root; Q3,
    resolved), then ingest it into an ordinary spec-conformant concept under the bundle's
    `references/`; cite the ingested concept, keeping the original URL as a courtesy
    field.
  - Lossy formats (PDF, video) are captured as a packet (original + `extracted.md`);
    everything else is captured flat.
- **SKILL-4 — Citation form.** Claims attributable to a source are footnoted with a label
  equal to the `sources[].id` (§5.1), never a positional reference and never a body
  citations list.
- **SKILL-5 — Stamping discipline.** An agent writing content updates `generated.{by,at}`
  only.
  - The skill never writes `verified` for its own generation (no self-verification).
  - A non-generating agent MAY record a machine-confirmed verification; human review is
    `okf verify`.
- **SKILL-6 — `raw/` immutability.** Both skills treat an ingested `raw/` item as
  read-only, tracked via its capture-manifest entry rather than by directory name inside a
  bundle (Q3, resolved): new evidence is a new file, never an edit of an existing one.
- **SKILL-7 — Custodian skill exists.** `skills/` contains a custodian skill covering
  discovery and enrichment (prose writing) for a maintained bundle.
  - It regenerates indexes via `okf index` rather than hand-editing them.
  - It records notable updates in `log.md` per §9.
  - It surfaces its output for review (draft status → `okf inbox`; CI runs open a PR),
    rather than silently landing machine-derived insight.
- **SKILL-8 — No hidden tool references.** Tooling a bundle depends on is wired through
  declarative frontmatter pointers (`executor.resource`, `attester.resource`, §10), never
  buried in prose (decisions §1).

## 3. CLI surface

Every row below is what `okf` dispatches today, and every verb `okf help` lists has a row.

| Command | Purpose | Key flags | Exit codes |
| --- | --- | --- | --- |
| `okf lint [path]` | Validate §11 conformance plus the configured warning set | `--json`, `--format`, `--severity <OKF####>=<level>`, `--treat-all-warnings-as-errors`, `--config`, `--list-rules` | 0 clean · 1 errors present · 2 usage/environment |
| `okf index [path]` | Generate `index.md` for every directory in a bundle | `--check` (write nothing; fail on drift), `--json`, `--format` | 0 written/no drift · 1 drift under `--check` · 2 usage |
| `okf search <query> [path]` | Search resolved bundles | `--scope`, `--type`, `--tag`, `--limit`, `--format`, `--json` | 0 (including no matches) · 2 usage |
| `okf inbox [path]` | List unacknowledged concepts (regenerated-since-verified, `draft`, stale or source-drifted) | `--json`, `--format`, `--fail-if-any` | 0 (whatever it finds) · 1 non-empty inbox under `--fail-if-any` · 2 usage |
| `okf verify <concept>...` | Stamp `verified: {by: human:<id>, at: now}` | `--by <actor>` (machine confirmation, Q12), `--dry-run`, `--config` | 0 stamped · 1 refused (no resolvable human id — `verify.actor` unset and no global git email, unknown concept) · 2 usage, or a refused self-verification |
| `okf capture <add\|close>` | Record an item already sitting in `raw/` in the capture manifest, or close its ingestion | `--by <actor>` (required), `--url`, `--title`, `--source-last-modified`, `--form flat\|packet`, `--concept` (`close`), `--captured-at` / `--at`, `--json` | 0 written · 1 the record says no (already captured and ingested, entry already closed, named concept absent) · 2 the manifest does not read as one, the item is not capturable, no actor, or usage |
| `okf generated stamp <concept>...` | Write the `generated: {by, at}` stamp a producer owes on every write | `--by <actor>` (required), `--at`, `--dry-run` | 0 stamped · 2 missing concept, frontmatter that does not parse, malformed actor, or usage |
| `okf init [name]` | Scaffold `okf/` project layout, first bundle, and project config | `--name`, `--personal` | 0 created · 1 refused (would overwrite) · 2 usage |
| `okf bundle [path]` | Package the vault's bundles for consume-only distribution (post-MVP, §5) | `--out`, `--format`, `--bundle`, `--lint`, `--generated-at`, `--verify` | 0 packaged/verified · 1 `--verify` mismatch or `--lint` errors · 2 usage |
| `okf site [path]` | Render the vault as a self-contained static site — landing page, trust dashboard, cross-link graph, one page per markdown file (post-MVP, §5) | `--out`, `--name`, `--single-file`, `--json`, `--format` | 0 generated · 2 usage, including an `--out` inside a bundle or at a bundle's parent |
| `okf skills <list\|path\|install>` | Report the agent skills this binary carries, where one is installed, or install them | `--host`, `--dir`, `--scope <user\|project>` (an install target, not a search scope), `--force` | 0 installed or reported · 1 `path` for a skill that is not installed · 2 usage |
| `okf mcp [path]` | Run the stdio MCP server (`okf_list`, `okf_search`, `okf_read`) | `--scope` (at launch only) | 0 clean shutdown · 2 startup failure |
| `okf upgrade` | Replace this binary with a release from the artifact host — the only command that uses a network, and only when it is run (`docs/architecture.md` AD-53) | `--check`, `--version <x.y.z>`, `--channel <stable\|rc>` (`rc` reserved), `--dry-run`, `--json`; `OKF_INSTALL_URL` | 0 upgraded, already current, or reported · 1 `--check` found an upgrade · 2 refused before writing: unusable base URL, unreadable manifest, no asset for this platform, digest mismatch, unwritable install directory |
| `okf version` · `okf help` | Report the informational version — the bare `<semver>` from a build stamped off a tag, `<semver>+<short-sha>` from an untagged one (#53); print the verb list | `--verbose` / `-v` on `version` adds a `commit: <sha>` second line; `--version`, `--help`, `-h` as aliases | 0 |
| `okf register [path]` | Add a vault or bundle root to the registry (idempotent) | `--verbose` | 0 registered or already registered · 2 usage |
| `okf unregister [path\|id]` | Remove a registry entry (idempotent) | `--verbose` | 0 removed or not registered · 2 usage |
| `okf registry [list\|prune]` | Report the registry, or drop the entries whose path is gone | `--json`, `--format`, `--verbose` | 0 reported or pruned · 2 usage |

Global flags apply to every command: `--verbose` (report vault resolution and effective
configuration), `--json` / `--format json` where output is data, and the configuration
overrides governed by CLI-4.

## 4. Acceptance and validation strategy

**BUILT (ACC-1 … ACC-7).** All seven are asserted by the suite and gated in CI. ACC-5's
dogfood bundle is `okf/bundles/okf-net/` (work items #19 and #20), checked by the `dogfood`
job's `okf lint okf/`, `okf index --check` and `okf/custodian/check-manifest.py`; ACC-6's
hook half is deliberately narrower than "runs in a hook" — no git hook invokes the `okf`
binary, because a `dotnet run` on every commit is how a hook gets deleted, and ACC-6 asks
that the path *can* run in one (`docs/architecture.md` AD-46).

- **ACC-1 — Foreign-bundle acid test.** `okf lint` reports all four bundles in
  `~/code/knowledge-catalog/okf/bundles/` — `acme_retail`, `ga4`, `stackoverflow`,
  `crypto_bitcoin` — as **conformant**, exiting 0 under default severity.
  - These bundles are checked as-is, unmodified, from their upstream location.
  - They exercise: `viz.html` and `.py` files in the tree, `references/` holding
    first-class concepts, `Attested Computation` concepts with `executor`/`attester`
    wiring, the `not:` producer extension, bare-mapping `verified`, and root indexes
    without `okf_version`.
  - Warnings are permitted and expected; any **error** on these bundles is a defect in
    okf-net, not in the bundles.
- **ACC-2 — Port fidelity.** `Okf.Core` matches the behavior of
  `okf/src/reference_agent/bundle/document.py` for `parse`, `serialize`, `validate`,
  `normalize_verified`, `trust_tier`, and `is_stale` (decisions §3: port, do not import).
  - A shared table of inputs and expected outputs covers each edge case in CORE-1, CORE-5,
    CORE-6, and CORE-7; the C# result equals the documented Python result for every row.
  - Divergences that are deliberate (e.g. serializer formatting) are recorded in the test
    as intentional, with the reason, rather than being silently accepted.
- **ACC-3 — Index parity.** `okf index` output on the reference bundles matches the
  structure produced by `reference_agent`'s index generator: grouped `#` sections,
  `* [Title](link) - description` bullets, subdirectory entries linking to child indexes.
  - Where our output deliberately differs (root `okf_version` frontmatter, deterministic
    subdirectory descriptions), the difference is enumerated and tested, not incidental.
- **ACC-4 — Round-trip corpus test.** Parse → serialize over every `.md` file in the four
  reference bundles preserves all frontmatter keys, their order, and the body.
- **ACC-5 — Dogfood bundle.** This repo carries its own OKF bundle documenting the toolset,
  maintained by the shipped custodian skill and linted by CI.
  - Its bundle root is a subdirectory, never `docs/` itself — `docs/decisions.md` and
    `docs/prd.md` have no frontmatter and would make `docs/` non-conformant
    (decisions §2's README trap).
  - CI runs `okf lint` and `okf index --check` on it; the dogfood bundle failing either is
    a release blocker.
  - The repo never contains Ringo's knowledge — only its own dogfood bundle.
- **ACC-6 — Hook and CI viability.** The lint and index-check paths run to completion in a
  `pre-commit`/`pre-push` hook and in the GitLab `validate` stage without network access,
  and their exit codes gate the pipeline per CLI-14.
- **ACC-7 — Determinism.** Running any command twice on an unchanged tree produces
  identical output and identical exit codes; index generation is byte-stable across
  machines.

## 5. Post-MVP roadmap

Rationale and open spikes for each item are logged in
[decisions.md](decisions.md#open-items-tracked-in-session-task-list-mirrored-here).

**Bundler. BUILT (2026-08-15, work item #5; see decisions.md, "the bundler milestone").**
`okf bundle [path] --out <file-or-directory>` packages a vault's bundles for consume-only
distribution as a tar.gz (default), a zip, or a plain directory, which between them cover
every shape §3 permits — §3's own three are "a git repository", "a tarball or zip archive
of the directory", and "a subdirectory within a larger repository", and a `dir`
distribution committed anywhere satisfies the first and the third. It ships `bundles/<name>/**` and one `okf-bundle.json` at the root; the custodian
machinery needs no stripping because topic 1 already put it *beside* the bundle rather than
inside it, and `raw/` stays a producer-side archive per Q3. Archives are deterministic
(sorted entries, fixed timestamps, normalized modes) so the same vault yields byte-identical
bytes, and the manifest records a `sha256` per file that `--verify` re-checks (exit 1 on a
mismatch). `--lint` runs the linter over the packaged result the way a consumer sees it —
default severities, no config, no vault.

The cross-bundle-reference spike is **resolved**: a link into a bundle the caller left out
is left **dangling** (§6.1 obliges every consumer to tolerate a broken link), warned about
per link on stderr, and recorded in the manifest's `externalLinks`. Vendoring the linked
concept was rejected for duplicating a document's trust state along with its text, and
refusing to package without a flag was rejected for blocking what the spec permits. Because
a distribution keeps the vault's `bundles/<name>/` layout, a link between two *packaged*
bundles still resolves, so the dangling case is exactly the subset case.

**Static site generator. BUILT (2026-08-15, work item #6; see decisions.md, "the static
site milestone").** `okf site [path] --out <dir>` renders the resolved vault as a
self-contained static site — a landing page carrying the bundle's index hierarchy, a trust
dashboard whose tiles and tags are clickable filters, a force-directed cross-link graph
coloured by trust tier, and one page per markdown file — with `--single-file` collapsing
the whole thing into one fragment-routed HTML file. It surfaces the trust and lifecycle
frontmatter as first-class UI: every status carries a colour, a glyph and a word, and the
one shape that means "press me" is the pill. No third-party JavaScript ships and no page
makes a network request, so the same output is hostable on GitLab Pages (the `pages` job,
default branch only) and openable straight from `file://`. It stayed in the monorepo: the
generator is `Okf.Core`'s, because index and site markdown are content the format defines
rather than adapter presentation.

**Custodian staleness-refresh and acknowledgment loop. BUILT (2026-08-15, work item #7; see
decisions.md, "the staleness-refresh and acknowledgment loop").** What shipped is the
derivation and the surfacing: `okf inbox` reports one row per concept with its reasons —
regenerated since verification, `status: draft`, no verification at all behind a non-human
generator, stale, or citing a source that moved — and exits 0 whatever it finds, with
`--fail-if-any` for a caller that wants a branch; `okf verify` stamps the human
acknowledgment; the scheduled `custodian-inbox` CI job publishes the JSON as an artifact.
What is deliberately *not* automated: nothing fetches release notes, nothing drafts, and no
job opens a merge request. The fetch-and-draft step is a procedure in
`skills/okf-custodian/SKILL.md` that a person starts — the custodian's product is a draft
and an explanation, and judging whether the draft is right is the part a person is for.
Built on CORE-15 and CLI-12.

**Pi shim and widget. Not built.** A thin TypeScript extension for the Pi host that shells
out to the CLI or speaks to `okf mcp`; no logic of its own. A Pi widget (UI surface)
follows the shim; the display decisions it was waiting on were made by the site generator
above — status carries a colour, a glyph and a word, and only a pill is pressable.

**Vectorization spike. RUN (2026-08-15, work item #8; `docs/spikes/2026-08-15-vectorization.md`),
layer deferred to [#24](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/24).** The
spike returned **GO-with-conditions**: sqlite-vec works from .NET, every package in the
graph is permissively licensed, and a NativeAOT publish is warning-clean — so Q10's
non-AOT fallback is not needed for it. The conditions are what defers it: the extension is
a `dlopen`-able object that cannot be embedded, so `okf` becomes three files instead of
one, and the NuGet package is a stale third-party alpha. An optional semantic index remains
strictly opt-in, a generated artifact and never authoritative; the bundle stays complete
without it. It would also supply a better near-duplicate detector than the shipped
heuristic (Q8).

## 6. Open questions

Genuinely undecided as of this document unless marked RESOLVED, in which case the
decision (and its rationale) is recorded in decisions.md.

- **Q1 — Subdirectory descriptions in generated indexes. RESOLVED (2026-08-14, see
  decisions.md).** Concept entries use frontmatter `description` (unchanged). Subdirectory
  entries use the `description` of that subdirectory's `about.md` when present; when
  `about.md` is absent, the subdirectory entry is emitted with no blurb. `okf index`
  remains deterministic and offline (CLI-16) — no LLM call, no fallback synthesis. The
  custodian may author `about.md` files later to enrich index output.
- **Q2 — Diagnostic identifier scheme. RESOLVED (2026-08-14, see decisions.md).**
  Diagnostic IDs are `OKF####` numeric codes, Roslyn-style, with reserved category ranges:
  conformance `00xx`, provenance `01xx`, trust `02xx`, hygiene `03xx`. Severity
  configuration (CLI-6) references rules by ID; documentation may additionally give kebab
  nicknames, which are prose only, never configuration keys. (The internal source may use
  one easter-egg constant name; it is not a shipped ID.)
- **Q3 — `references/` semantics collision. RESOLVED (2026-08-14, see decisions.md).**
  Reintroduces a `raw/` layer **outside** bundle roots: `<project>/okf/raw/` (sibling of
  `bundles/`; personal vault `~/okf/raw/`). `raw/` is the drop zone — users drop artifacts
  there and the custodian pulls external material there too. Ingestion turns `raw/` items
  into ordinary spec-conformant concepts under the bundle's `references/`, which now
  carries **no** okf-net-specific semantics — spec §6.3's meaning is restored and foreign
  bundles are unaffected. Immutability applies to `raw/` items after ingestion, tracked via
  a capture manifest, not by directory name inside a bundle. (Dropping bare `.md` files
  inside a bundle root would be frontmatter-less "concepts" and break §11 conformance;
  also, the bundler ships only `bundles/`, so `raw/` originals are producer-side archive
  and distributed bundles carry only the extracted `references/` concepts plus
  original-URL frontmatter — an accepted trade-off.)
- **Q4 — Config file names and format. RESOLVED (2026-08-14, see decisions.md).** The
  global config file is `~/.config/okf/okf.json`. Precedence: CLI args > `OKF_HOME`
  environment variable > project config > global file. (The project config's exact
  filename and its placement relative to `okf/` remain implementation detail, not blocked
  on this resolution.) **Superseded on two points by what shipped (2026-08-15, see
  decisions.md, "the `okf lint` milestone", and `docs/architecture.md` AD-31, AD-32):**
  `OKF_HOME` is **not** a precedence layer — it moves which personal vault is read and
  never a rule's severity — and the chain that ships has four layers, built-in defaults
  first: defaults → global file (`XDG_CONFIG_HOME`, else `~/.config/okf/okf.json`) →
  project file → CLI arguments. The project config's filename is settled too: `okf.json` at
  the vault root, parsed as JSONC.
- **Q5 — Are any warnings exempt from promotion? RESOLVED (2026-08-14, see
  decisions.md).** No warning is exempt. okf-net adopts Roslyn's four severities
  (hidden/info/warning/error) for all diagnostics. Broken internal links default to
  **info**, not warning. "Never an error" describes the *default* severity only;
  consumer configuration may promote any diagnostic, including broken internal links —
  consistent with the standing principle that defaults block only spec conformance, and
  every additional block is consumer choice (their vault, their rules).
- **Q6 — Human identity for `okf verify`. RESOLVED (2026-08-14, see decisions.md).**
  `okf verify` stamps `human:<id>` from `verify.actor` in okf config. When unset, it falls
  back to `git config --global user.email` — deliberately the **global** git config only,
  never repo-local, because repo-local `user.email` is frequently an agent identity (this
  repo is the example: local `user.email` is `ringo.harrison+agent@gmail.com`). This
  replaces CLI-13's hard-refusal assumption with a fallback chain; refusal is now reserved
  for the case where both `verify.actor` and the global git email are unset.
- **Q7 — Search semantics and output contract. RESOLVED (2026-08-14, see decisions.md).**
  Option B: **deterministic BM25-style lexical search** — tokenized (not substring)
  matching, frontmatter weighted above body text (title ×3, tags/`type` ×2, `description`
  ×2, body ×1), query terms AND-ed with an OR fallback when AND matches nothing, ties
  broken by path so the same corpus always yields the same order. Four constraints come
  with it, from the disclosure-versus-search research:
  1. **Concepts only.** The corpus is concept documents inside bundle roots. `raw/` is
     never searched (it sits outside every bundle root, Q3) and the reserved files
     `index.md`/`log.md` are never corpus entries.
  2. **Project scope by default** (CLI-3, the topic-6 determinism rule): the same vault
     resolution `okf lint` performs, and nothing wider. Registry entries stay opt-in.
  3. **Links-first results.** A result carries a path, a title, a type, a score and a
     bounded snippet — never a full body. Reading a concept is `okf` `read`'s job
     (CORE-12), so search stays a pointer into progressive disclosure rather than a
     substitute for it.
  4. **Trust-aware fields.** Every result carries its trust tier (CORE-6) and stale flag
     (CORE-7), so a consuming agent can judge a hit before opening it.

  Filter syntax is `tag:<x>` and `type:<y>`, repeatable and case-insensitive, applied
  before scoring; `--tag`/`--type` flags are the same filters by another spelling. The
  JSON result record is engine-agnostic — nothing in it names BM25 — so the deferred
  vectorization spike (sqlite-vec, §5) can be slotted behind the same contract without a
  breaking change for MCP consumers.
- **Q8 — Near-duplicate detection in MVP. RESOLVED by what shipped (2026-08-15, see
  decisions.md, "the `okf lint` milestone" and "lint review flags").** `OKF0303` ships a
  normalized title-or-filename collision — not shingling, not similarity scoring — and a
  conventional filename (`about.md`) is exempt from both arms of it (AD-47). The id is
  kept when the vectorization layer (#24) replaces the heuristic, so consumer configuration
  survives the swap.
- **Q9 — Detecting a CI-authored commit. RESOLVED by not shipping the rule (2026-08-15).**
  No reliable signal was decided, so the "human actor on a CI commit" warning has no rule
  id and no implementation; it is absent from the shipped catalog (`OKF0001`–`OKF0004`,
  `OKF0101`–`OKF0103`, `OKF0201`–`OKF0202`, `OKF0301`–`OKF0310`). CLI-7's table row records
  the intent, not a rule that fires. Reopening it means picking a signal first.
- **Q10 — .NET target and AOT viability. PARTIALLY RESOLVED (2026-08-14, see
  decisions.md).** Target framework is `net10.0` (mise pins `dotnet = "10"`). NativeAOT
  single-file is preferred; if it proves incompatible (e.g. Roslyn-based extensibility
  added later, or a reflection-heavy dependency), the toolset falls back to a trim-safe,
  self-contained non-AOT publish knowingly, per decisions §3. Distribution target is NuGet
  packages published to the self-hosted GitLab instance's built-in NuGet registry
  (instance configuration to be verified at first publish). ~~**Still open:** the
  AOT-compatible YAML serialization library choice — no packaging or publish job exists in
  `.gitlab-ci.yml` yet, and that choice gates whether AOT is actually achievable.~~
  **The YAML half is RESOLVED (2026-08-15, see decisions.md, "YAML library"):** YamlDotNet
  18.1.0, used through its representation model and event emitter only, never its
  serializer — that path is reflection-free, so a real `PublishAot` publish of `Okf.Cli` is
  zero-warning and AOT is achievable (AD-9, AD-10). **The RIDs are RESOLVED (2026-08-15,
  work item #36):** three ship — `linux-x64` NativeAOT, `osx-arm64` and `win-x64` trim-safe
  self-contained, because NativeAOT compiles through the host's toolchain and the only
  runner is Linux. AOT for the other two needs a macOS and a Windows runner
  ([#38](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/38),
  [#39](https://gitlab.tychostation.dev/ringo/okf-net/-/issues/39)). **Still open:** the
  `Okf.Core` NuGet package — no packaging step for it exists in `.gitlab-ci.yml`, and the
  instance's registry configuration is still unverified. `osx-x64`, musl and `linux-arm64`
  are each one line in the publish job and one case label in `install.sh`, and are not
  built on speculation.
- **Q11 — `okf verify` and `okf inbox` scope granularity. RESOLVED (2026-08-15, see
  decisions.md).** `okf inbox [path]` resolves its working set exactly as `okf lint` and
  `okf search` do (CLI-1), so the three never disagree about which bundles they are looking
  at; the default is therefore the project vault. `okf verify` takes concept paths, one or
  many, because a verification stamp names a document a person actually read — no directory
  and no glob. Neither reaches the registry. `okf search` and `okf mcp` grew `--scope` in
  work item #43; `okf inbox` and `okf verify` deliberately did not, because an inbox is a
  queue somebody works through and a stamp names a file somebody opened — neither is
  improved by spanning vaults nobody is reviewing. Widening either is a separate decision.
- **Q12 — Machine verification surface. RESOLVED (2026-08-15, see decisions.md).** A
  `--by <actor>` flag on `okf verify`, not a separate command: the refusals a machine
  confirmation needs — a well-formed §7 actor, never the concept's own `generated.by` —
  are the ones `okf verify` already performs, so a second command would be the same code
  behind another name. Plain `okf verify` remains human-only, and a `verify.actor` that
  names a process or a tool is a configuration error rather than a coerced `human:` stamp.
