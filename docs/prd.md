# okf-net — Product Requirements

> **Status:** MVP requirements. Rationale for every decision referenced here lives in
> [decisions.md](decisions.md); this document states *what must be built and how it is
> verified*, not *why*. Where the two disagree, decisions.md wins and this document is
> wrong.
>
> Format under implementation: OKF v0.2, specified in
> `~/code/knowledge-catalog/okf/SPEC.md` (referred to below as "the spec", cited by
> section, e.g. §11).

## 1. Overview

okf-net is a .NET toolset for producing, validating, and consuming **OKF v0.2 knowledge
bundles**: directory trees of markdown files with YAML frontmatter. It ships as a
library (`Okf.Core`), a single CLI binary (`okf`) that also hosts an MCP server
(`okf mcp`), and a pair of agent skills.

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

The following are explicitly **out of scope** for MVP and tracked as roadmap in §5:

- **Bundler** — packaging a bundle for consume-only distribution. (Out of scope for MVP;
  built post-MVP as `okf bundle` — see §5.)
- **Static site generator** — rendering a bundle as a browsable site.
- **Pi extension / widget** — the TypeScript shim and any UI surface.
- **Vectorization / semantic index** — sqlite-vec or equivalent.
- **Custodian staleness-refresh loop** — automated re-derivation of expired concepts and
  the acknowledgment workflow around it.

Also out of scope, permanently or until a concrete need appears: executing executors or
attesters (§10 is *recorded and surfaced*, never run by okf-net), defining a type
taxonomy, and any storage or serving layer beyond the filesystem.

## 2. MVP functional requirements

Requirements are testable statements with acceptance criteria. IDs are stable; future
work references them.

Build order is fixed by decisions §"MVP build order": Core parse/validate → `okf index` →
`okf search` → `okf mcp` → skills.

### 2.1 Okf.Core

`Okf.Core` holds **all** logic. The CLI and MCP server are thin adapters over it
(decisions §4). Every requirement below is satisfied by the library and is unit-testable
without a process boundary.

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
  project vault, the bundle roots inside it, and registered vaults.
  - Walk up from the start directory for a directory named `okf/`; the nearest one wins.
  - `OKF_HOME` overrides the personal-vault location; the default personal vault is
    `~/okf/` (visible, not hidden).
  - A bundle root is a directory under `<vault>/bundles/`; a directory pointed at
    directly (a foreign bundle) is also a valid bundle root.
  - Discovery is pure: it reads the filesystem and configuration and performs no writes.
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
hook: single process, no daemon, no network, meaningful exit code.

- **CLI-1 — Vault resolution.** Every command resolves its working set the same way.
  - Default target is the project vault found by walking up for `okf/`.
  - With no project vault and no explicit path, the command targets the personal vault
    (`OKF_HOME`, else `~/okf/`).
  - An explicit path argument overrides discovery and may point at any bundle root,
    including a foreign one.
  - Resolution is reported in `--verbose` output so "which bundle did it read" is never a
    guess.
- **CLI-2 — Registry.** `~/.config/okf/` holds the registry of known bundles.
  - `okf register [path]` adds an entry; it is idempotent (re-registering the same path is
    a no-op success).
  - `okf unregister [path]` removes an entry; removing an unknown entry is a no-op
    success.
  - The personal vault is an ordinary registry entry with no special-casing
    (decisions §6).
  - Auto-registration is **off** by default; a global setting opts into it.
- **CLI-3 — Search scope.** Search defaults to project-only.
  - With a project vault resolved, only that vault's bundles are searched — identical
    results on every machine and in CI.
  - Registry entries (including the personal vault) are included only when configuration
    or an explicit flag opts them in.
- **CLI-4 — Configuration precedence.** CLI args > `OKF_HOME` environment variable >
  project config > global config at `~/.config/okf/okf.json` (decisions §6, Q4 resolved).
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
  | Human actor on CI commit | `generated.by` is a `human:` actor on a CI-authored commit | warning (see Q9) |
  | Missing source resource | A `sources[]` entry carries no `resource`, which §5.1 requires within an entry | warning |
  | Unresolvable source resource | A `sources[].resource` written as a path names nothing inside the bundle (§6.2); absolute URLs and §5.1 scope descriptors are never checked | info |
  | Link leaves the bundle | A markdown link resolves outside the bundle root (§6.2), which was previously unreported and so indistinguishable from a correct link | info |
  | Raw item mutated | An ingested `raw/` item no longer matches the `sha256` its capture-manifest entry records (CLI-9) | warning |

  The three rows above the last were added from the dogfood friction log (work item #21,
  from #19's note_168); see decisions.md, "the lint/search friction milestone". The last
  is CLI-9's `raw/`-immutability rule, which landed with `okf init` (work item #4).

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
- **CLI-16 — Offline and hermetic.** No command makes a network call or invokes a model.
  - The full MVP surface runs with networking disabled.
- **CLI-17 — Distribution.** The CLI ships as a self-contained, single-file binary
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

Two skills ship in `skills/` with releases (decisions repo layout). They are prose
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

| Command | Purpose | Key flags | Exit codes |
| --- | --- | --- | --- |
| `okf lint [path]` | Validate §11 conformance plus the configured warning set | `--json`, per-rule severity override, `--treat-all-warnings-as-errors` | 0 clean · 1 errors present · 2 usage/environment |
| `okf index [path]` | Generate `index.md` for every directory in a bundle | `--check` (write nothing; fail on drift) | 0 written/no drift · 1 drift under `--check` · 2 usage |
| `okf search <query> [path]` | Search resolved bundles | `--type`, `--tag`, `--limit`, `--format`, `--json`, scope opt-in | 0 (including no matches) · 2 usage |
| `okf inbox` | List unacknowledged concepts (regenerated-since-verified or `draft`) | `--json` | 0 · 2 usage |
| `okf verify <concept>...` | Stamp `verified: {by: human:<id>, at: now}` | — | 0 stamped · 1 refused (no resolvable human id — `verify.actor` unset and no global git email, unknown concept) · 2 usage |
| `okf register [path]` | Add a bundle/vault to the registry (idempotent) | — | 0 · 2 usage |
| `okf unregister [path]` | Remove a registry entry (idempotent) | — | 0 · 2 usage |
| `okf init [name]` | Scaffold `okf/` project layout, first bundle, and project config | — | 0 created · 1 refused (would overwrite) · 2 usage |
| `okf bundle [path]` | Package the vault's bundles for consume-only distribution (post-MVP, §5) | `--out`, `--format`, `--bundle`, `--lint`, `--generated-at`, `--verify` | 0 packaged/verified · 1 `--verify` mismatch or `--lint` errors · 2 usage |
| `okf mcp` | Run the stdio MCP server (search/read/list) | — | 0 clean shutdown · 2 startup failure |

Global flags apply to every command: `--verbose` (report vault resolution and effective
configuration), `--json` where output is data, and the configuration overrides governed by
CLI-4.

## 4. Acceptance and validation strategy

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

**Static site generator.** Replaces the Obsidian dependency with a generated browsable
site; GitLab Pages and plain file handoff are the target outputs, with
`reference_agent visualize`'s self-contained `viz.html` as prior art. Must surface the
OKF v0.2 trust and lifecycle frontmatter — trust tier, `stale_after`, verified-by-agent
versus verified-by-human — as first-class UI, not buried metadata. **Pending input from
Ringo on exactly how those fields should be displayed.** Likely the first component to
split out of the monorepo, on release-cadence grounds.

**Custodian staleness-refresh and acknowledgment loop.** When a version-pinned source's
`stale_after` expires, the custodian fetches release notes from the pinned version to
current, updates or drafts the affected concept, and derives an impact analysis. Output
surfaces as `status: draft` → `okf inbox` → a CI-opened PR, making the PR the reviewable
(and ignorable) inbox for machine-derived insight. Builds directly on CORE-15 and CLI-12.

**Pi shim and widget.** A thin TypeScript extension for the Pi host that shells out to the
CLI or speaks to `okf mcp`; no logic of its own. A Pi widget (UI surface) follows the
shim and depends on the same display decisions as the site generator.

**Vectorization spike.** An optional semantic index (sqlite-vec or similar), strictly
opt-in, used only as a fallback when progressive disclosure fails to surface the right
concept. The index is a generated artifact and never authoritative; the bundle remains
complete without it. Would also supply a better near-duplicate detector than the MVP
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
  on this resolution.)
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
- **Q8 — Near-duplicate detection in MVP.** With vectorization deferred, what heuristic
  backs the near-duplicate warning: title/description similarity, shingled body hashing, or
  drop the rule from MVP and reintroduce it with the vectorization spike.
- **Q9 — Detecting a CI-authored commit.** The "human actor on CI commit" warning needs a
  reliable signal (`CI=true`, a GitLab-specific variable, an explicit `--ci` flag, or the
  commit's committer identity). Without one the rule cannot fire correctly in a local hook.
- **Q10 — .NET target and AOT viability. PARTIALLY RESOLVED (2026-08-14, see
  decisions.md).** Target framework is `net10.0` (mise pins `dotnet = "10"`). NativeAOT
  single-file is preferred; if it proves incompatible (e.g. Roslyn-based extensibility
  added later, or a reflection-heavy dependency), the toolset falls back to a trim-safe,
  self-contained non-AOT publish knowingly, per decisions §3. Distribution target is NuGet
  packages published to the self-hosted GitLab instance's built-in NuGet registry
  (instance configuration to be verified at first publish). **Still open:** the
  AOT-compatible YAML serialization library choice — no packaging or publish job exists in
  `.gitlab-ci.yml` yet, and that choice gates whether AOT is actually achievable.
- **Q11 — `okf verify` and `okf inbox` scope granularity. RESOLVED (2026-08-15, see
  decisions.md).** `okf inbox [path]` resolves its working set exactly as `okf lint` and
  `okf search` do (CLI-1), so the three never disagree about which bundles they are looking
  at; the default is therefore the project vault. `okf verify` takes concept paths, one or
  many, because a verification stamp names a document a person actually read — no directory
  and no glob. Neither reaches the registry, for the same reason `okf search` does not:
  `okf register` does not exist yet.
- **Q12 — Machine verification surface. RESOLVED (2026-08-15, see decisions.md).** A
  `--by <actor>` flag on `okf verify`, not a separate command: the refusals a machine
  confirmation needs — a well-formed §7 actor, never the concept's own `generated.by` —
  are the ones `okf verify` already performs, so a second command would be the same code
  behind another name. Plain `okf verify` remains human-only, and a `verify.actor` that
  names a process or a tool is a configuration error rather than a coerced `human:` stamp.
