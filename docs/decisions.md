# okf-net — Architecture Decisions (running log)

> **Status:** living document. This is the raw material for a future PRD.
> Captured from design sessions between Ringo and Claude (Claude Code), starting 2026-08-14.
> Reference implementation studied: Google's [knowledge-catalog](https://github.com/GoogleCloudPlatform/knowledge-catalog) repo (local clone at `~/code/knowledge-catalog`), especially `okf/SPEC.md` (OKF v0.2), `okf/src/reference_agent/`, `okf/bundles/acme_retail/`, and `toolbox/mdcode` + `toolbox/enrichment`.

## Vision

A personal + team knowledge system built on **OKF v0.2** (Open Knowledge Format: markdown + YAML frontmatter bundles, domain-first organization). Rejected pi-llm-wiki's epistemic-type folder layout (concepts/entities/syntheses/analyses) in favor of what the spec actually grants: SPEC §3 makes directory structure *independent of the domain* — producers organize however makes sense — and mandates no tree shape at all. Domain-first is **okf-net's own convention**, layered onto that freedom (an earlier wording here attributed domain-first to the spec itself; corrected 2026-08-14 during the dogfood content review). What the spec does fix: document kind lives in frontmatter `type`; cross-cutting categorization via `tags` (§4.1).

End state: agents do **progressive discovery** across a registry of bundles (personal KB + per-project bundles) — e.g., during an app discovery phase, an agent walks bundle indexes and recommends tools/integrations from accumulated knowledge. Future ingestion source: Ringo's exported Claude.ai conversation history.

Key vocabulary:

- **Custodian** — the agent/process that maintains a bundle (discovery + enrichment/prose-writing). Producer-side. Runs via git hooks and CI.
- **Bundler** — packages a bundle for consume-only distribution (strips custodian machinery).
- **Prose vs structured**: frontmatter = the few machine-queryable fields; markdown body = prose for humans/LLMs. Code operates on the structured layer; agents operate on the prose layer.

## Decision log

### 1. Where agents/tooling live

- **Consumers are consume-only.** A bundle is readable markdown; consumption never requires executing anything.
- **Custodian machinery (skill + Python scripts + config) lives in the project repo** beside the bundle, git-hook/CI triggered — but the shared *toolset* (this repo) is referenced by version, never vendored per-project (avoids version skew).
- Pattern stolen from spec §10 / acme_retail: **declarative pointers in frontmatter** (`executor.resource`, `attester.resource`) wire concepts to skills/scripts — never prose-buried tool references. Prose instructions + small deterministic scripts may live in-bundle; intelligence lives outside.
- The bundle *describes* its own tooling as concepts (see #2) rather than shipping it.

### 2. Bundle self-description & project layout

- **OKF requires no specific files.** `index.md`/`log.md` are optional *reserved* names (§8/§9). Conformance (§11) = every non-reserved `.md` has parseable frontmatter with non-empty `type`.
- **README.md trap:** README is NOT reserved → inside a bundle root it would be a frontmatter-less concept → non-conformant. Therefore **bundle root ≠ repo root**.
- **Per-project layout:**

  ```text
  <project>/okf/
    README.md          # repo-facing docs, safely outside bundle roots
    bundles/<name>/    # one or many bundle roots
    custodian/         # skill + prompt + recipe config + hook scripts (strippable)
    raw/               # drop zone for captured artifacts (see "Open-question
                        # resolutions" below; supersedes the no-raw-layer call in §5)
  ```

- **Entry-point convention** for bundles we produce: root `index.md` always generated, carries `okf_version: "0.2"` frontmatter (§12); its first entry links an `about-this-bundle.md` concept (type: Guide/Playbook) naming the toolset, custodian, update cadence, and consumption options (plain reading / MCP).
- For foreign bundles without an index, our tooling synthesizes one on the fly (spec-sanctioned).

### 3. Languages

- **Toolset (this repo): .NET / C#.** AOT single-file binaries while possible (`curl | sh` distribution); drop to self-contained non-AOT if Roslyn-based extensibility is ever added (cost: size + cold start only). Motivations: maintainer passion, binary distribution means language never leaks to consumers, sparking .NET community interest (outreach idea: Milan Jovanović, Nick Chapsas). okf-gem (Ruby) as precedent that the format is the interop layer.
- **In-bundle custodian scripts: Python** — arbitrary consumer/CI environments can run them.
- **Pi extension: TypeScript** (Pi is a TS host) — thin shim shelling out to the CLI / MCP. Pi widget = post-MVP.
- Port `okf/src/reference_agent/bundle/document.py` logic (validate / trust_tier / is_stale, bare-mapping `verified` normalization) to C# rather than importing.

### 4. Layering: library is the core

- **All logic in `Okf.Core`. CLI is a thin wrapper. MCP server is a thin protocol wrapper launched as `okf mcp`** (kcmd precedent: `kcmd mcp` is a CLI subcommand). Neither CLI nor MCP is "the core."
- Rationale: git hooks and CI need a plain process (parse files, exit) — not JSON-RPC to a server. MCP is an always-available *adapter*, not the center. Skills teach agents to call the CLI (post-MCP-bloat industry pattern).

### 5. Provenance: capture vs cite

- Decision test: **"If this source changed or vanished tomorrow, could the custodian still re-verify the concept?"**
  - Yes (repo code, own DB, context7/version-pinned docs, git tag) → **cite** via `sources[].resource` (+ `last_modified`, version pin). Spec-native (§5.1, incl. scope descriptors).
  - No (blog posts, videos, PDFs, saved HTML) → **capture into `references/`**, cite the captured copy; original URL kept as courtesy field.
- `references/` = archive of evidence that can't defend itself. It is the **read-only zone** (the one pi-llm-wiki guardrail kept). No separate `raw/` layer.
  - **Superseded by [Open-question resolutions (2026-08-14)](#open-question-resolutions-2026-08-14), Q3.** A separate `raw/` layer outside bundle roots was reintroduced; `references/` reverts to plain spec §6.3 semantics and immutability is now tracked on `raw/` items via a capture manifest, not by directory name. Left as-is here for history — see that section for the current rule.
- **Flat by default; packet (original artifact + `extracted.md`) only for lossy formats** (PDF, video).
- Compensating control for cited-live sources: `stale_after` + source `last_modified` vs concept `generated.at` drift checks.

### 6. Vaults, registry, config

- **Personal vault: `~/okf/` (visible, not hidden).** Project vault found by walk-up for `okf/`. `OKF_HOME` override.
- **Registry** (`~/.config/okf/`) lists known bundles. **The personal vault is itself just a registry entry** — no special-casing; everything beyond the current project enters through the registry.
- **Search defaults to project-only** (team determinism: identical behavior on every machine + CI). Registry entries (incl. personal) are opt-in via config.
- **Config precedence: CLI args > project config (committed = team contract) > global JSON in `~/.config/okf/`.**
- **Auto-register OFF by default.** Explicit idempotent `okf register` (+ `okf unregister`); global opt-in setting to automate.

### 7. Write discipline & lint

- **Default hard errors = OKF §11 conformance only** (parseable frontmatter, non-empty `type`, reserved-file structure). Principle: *defaults block only what the spec says; every additional block is consumer configuration.*
- **Roslyn-style severity config:** any warning promotable to error; `treatAllWarningsAsErrors` supported.
- `okf init` scaffolds project config that promotes to errors: `references/` mutation after capture; hand-edit drift in generated files (`index.md` vs what `okf index` would emit).
- **Warnings:** citation integrity (footnote label ↔ `sources[].id` join); staleness (`today >= stale_after`; source `last_modified` > `generated.at`) — **staleness never blocks by default**; broken internal links (spec-tolerated, never error); near-duplicate concepts; missing `description`; missing `tags` (opt-in); tags not in bundle tag registry (opt-in, beyond-spec extension); **self-verification** (`verified[].by == generated.by`); `human:` actor in `generated.by` on CI-authored commits.
- **Stamping:** agent writes update `generated.{by,at}` only. Trust tiers per §5.3: unverified / machine-confirmed (non-human verifiers) / human-reviewed. Agents MAY verify (machine-confirmed) but never the generating agent itself. `okf verify <concept>` stamps `verified: {by: human:<id-from-config>, at: now}`.
- Acknowledgment model (ties to custodian refresh): **"unacknowledged" = `generated.at` newer than latest `verified.at`, or `status: draft`** — no new frontmatter field needed. `okf inbox` lists; `okf verify` clears; `log.md` records; CI custodian opens PRs (the PR is the reviewable/ignorable inbox for machine-derived insights).

## Repo layout (this repo)

```text
okf-net/
├─ src/
│  ├─ Okf.Core/        # ALL logic: parse, validate, trust, staleness, index, search
│  └─ Okf.Cli/         # thin: okf lint|index|search|inbox|verify|register|mcp|bundle|site
├─ skills/             # custodian + capture skills shipped with releases
├─ tests/
└─ docs/               # this file; later a dogfood okf/ bundle documenting the toolset itself
```

Monorepo until a component needs its own release cadence (site generator likely first split). This repo never contains Ringo's knowledge — only its own dogfood bundle.

## MVP build order

1. `Okf.Core` parse/validate (the okf-lint)
2. `okf index` generation
3. `okf search`
4. `okf mcp`
5. Capture + custodian skills

Steps 1–3 make bundles usable by any agent (Bash-callable CLI) with nothing else built. Post-MVP: bundler, site generator, Pi shim/widget, vectorization spike.

Process note: BMAD judged overkill for MVP (SPEC.md is the PRD; these sessions were the architecture phase). Decisions land in docs → later the dogfood bundle. BMAD-style story-driven dev acceptable for implementation phase if desired.

## Open items (tracked in session task list, mirrored here)

- **Bundler** — strip custodian machinery for distribution; SPIKE: how to package cross-bundle concept references (inline? vendor? dangling-link?).
- **Static site generator** — replace Obsidian dependency; GitLab Pages/file-handoff targets; prior art: `reference_agent visualize` self-contained viz.html. **Ringo has more to add on displaying OKF v0.2 fields (trust tiers, stale_after, verified-by-agent-vs-human) on the site.**
- **Custodian staleness-refresh loop** — on `stale_after` expiry for version-pinned sources: fetch release notes current→latest, update/draft concept incl. derived impact analysis; surfaces via draft status + inbox + CI PR.
- **Vectorization spike** — optional sqlite-vec (or similar) semantic index; strictly opt-in; fallback only when progressive disclosure fails; generated artifact, never authoritative.
- **Side project** — misspelling/vocabulary coaching skill (cross-session log + periodic coaching).

## Useful reference paths (Google repo clone)

- `okf/SPEC.md` — OKF v0.2 spec (§5 trust families, §8/§9 reserved files, §10 attested computations, §11 conformance, §12 versioning)
- `okf/src/reference_agent/bundle/document.py` — canonical validate/trust_tier/is_stale to port
- `okf/bundles/acme_retail/` — worked example incl. `not:` frontmatter extension (anti-definitions), executor/attester wiring
- `okf/samples/*/` — recipe pattern (seeds + exact command) ≈ custodian config
- `toolbox/mdcode/` — library→CLI→MCP layering precedent (`kcmd mcp`); `docs/concept.md` has the `kb` scope / Documents Layout (Google converging on markdown-first)
- `toolbox/enrichment/` — minimal enrichment harness: prompt + MCP tools dir + skills dir + per-item loop + one write-tool
- `python -m reference_agent visualize --bundle <path>` — works on any conformant bundle, no GCP creds

## Open-question resolutions (2026-08-14)

Ringo's resolutions for several of PRD §6's open questions. Each is also reflected at its
question in `prd.md` §6.

- **Q1 (index subdirectory descriptions).** Concept entries use frontmatter `description`.
  Subdirectory entries use the `description` of `<subdir>/about.md` when present; else the
  entry is emitted with no blurb. `okf index` stays deterministic and offline; the
  custodian may author `about.md` files later.
- **Q2 (diagnostic IDs).** `OKF####` numeric IDs, Roslyn-style, with reserved category
  ranges: conformance `00xx`, provenance `01xx`, trust `02xx`, hygiene `03xx`. Severity
  config references IDs; docs may give kebab nicknames. (The internal source may use one
  easter-egg constant name; it is not a shipped ID.)
- **Q3 (`references/` collision).** Reintroduce a `raw/` layer **outside** bundle roots:
  `<project>/okf/raw/` (sibling of `bundles/`; personal vault `~/okf/raw/`). `raw/` is the
  drop zone: users drop artifacts there; the custodian also pulls external material there.
  Ingestion turns `raw/` items into ordinary spec-conformant concepts under the bundle's
  `references/`, which now carries **no** okf-net-specific semantics (spec §6.3 meaning
  restored, foreign bundles unaffected). Immutability applies to `raw/` items after
  ingestion, tracked via a capture manifest — not by directory name inside bundles.
  Placement rationale: dropped `.md` files inside a bundle root would be frontmatter-less
  "concepts" and break §11 conformance; also the bundler ships only `bundles/`, so
  originals are producer-side archive and distributed bundles carry the extracted
  `references/` concepts plus original-URL frontmatter (accepted trade-off). This
  supersedes the earlier "`references/` is the read-only zone / no separate `raw/` layer"
  decision in §5 above — see the superseded-by note there.
- **Q4 (config).** Global config file is `~/.config/okf/okf.json`. Precedence: CLI args >
  `OKF_HOME` env var > project config > global file.
- **Q5 (broken-links contradiction).** Adopt Roslyn's four severities (hidden/info/warning/
  error) for all diagnostics. Broken internal links default to `info`. "Never an error"
  applies to DEFAULTS only; consumer config may promote any diagnostic (their vault, their
  rules) — consistent with the standing principle that defaults block only spec
  conformance.
- **Q6 (verify actor).** `okf verify` stamps `human:<id>` from `verify.actor` in okf
  config; default fallback is `git config --global user.email` — deliberately GLOBAL,
  never repo-local, because repo-local email is often an agent identity (okf-net itself is
  the example: local `user.email` is `ringo.harrison+agent@gmail.com`).
- **Q10 (partial).** Target framework `net10.0` (mise now pins dotnet 10). NativeAOT
  preferred; if incompatible (e.g. Roslyn extensibility later, or reflection-heavy deps),
  fall back to trim-safe self-contained non-AOT. Distribution: publish NuGet packages to
  the self-hosted GitLab instance's built-in NuGet registry (instance configuration to be
  verified at first publish). Serialization-library choice (AOT-compatible YAML) remains
  open — that part of Q10 is left open.

### Proposed decision (pending review): YAML library

- **YamlDotNet, used through its representation model and event emitter only — never its
  serializer.** Candidates were YamlDotNet (with the `[YamlStaticContext]` source generator
  for AOT) and VYaml (source-generated, AOT-first). VYaml's strength is fast typed
  serialization of known shapes; okf-net needs the opposite (PRD CORE-2): frontmatter must
  round-trip with unknown producer keys, insertion order, and scalar form intact, which
  means a *structure-preserving* parse, not deserialization into types. YamlDotNet's
  `YamlStream`/`YamlNode` and its `Emitter` do exactly that, and both are reflection-free —
  so the AOT question never reaches the static-context source generator at all: `Okf.Core`
  builds with `IsAotCompatible=true` and no trim or AOT warnings, because no
  reflection-based serializer is on the path. The dependency is confined to one internal
  file (`src/Okf.Core/YamlBridge.cs`); the public API exposes only okf-net's own `OkfValue`
  model, so swapping engines later is an implementation change, not a breaking one.
  **Still to verify:** an actual `PublishAot=true` publish, which needs the CLI (CLI-17)
  and is the only real proof for Q10.

### Proposed decisions (pending review): reviewer flags from the Okf.Core port review (2026-08-14)

- **`type: 0` — §11 vs ACC-2 (decides lint behavior).** Spec §11 says "non-empty
  `type`"; the reference implementation applies Python truthiness, so `type: 0`,
  `type: false`, `type: no` count as *missing*. These conflict, and `okf lint` must
  pick one. **Proposal: follow the reference implementation (ACC-2)** — interop
  parity with Google's own tooling beats a literal §11 reading, and no real bundle
  puts falsy scalars in `type`. Revisit if the spec clarifies.
- **YAML alias/anchor node sharing (hazard, deferred).** `a: &x {...}` / `b: *x`
  parse to the *same* mutable node; in-place stamping (CORE-14) through one path
  would silently edit the other. No reference bundle uses anchors. Proposal: before
  CORE-14 ships, deep-copy shared nodes at parse time (or reject anchors with a
  diagnostic). Do not ship stamping without one of the two.
- **YAML tags are silently dropped** (`!!str 5` re-emits as `5`) — a retype under a
  strict CORE-2 reading. No bundle uses tags. Proposal: accept as a documented
  limitation until a real producer emits tags.
- **`OkfValue.IsTruthy` is public** and named for a Python concept. Proposal: narrow
  to internal (or rename to a §11-shaped name) before v1 freezes the API.
- **Duplicate frontmatter keys are rejected** (YamlDotNet) where PyYAML reads
  last-wins — pinned as a deliberate sixth deviation by test; rejecting is the
  stricter, better §11 behavior.

### Proposed decisions (pending review): the `okf lint` milestone (2026-08-14)

- **Project config file: `<project>/okf/okf.json`** — the vault root, sibling of
  `bundles/`, `custodian/`, and `raw/`. Q4 fixed the *global* file
  (`~/.config/okf/okf.json`) and left the project file's name and placement as
  implementation detail. Proposal: same filename, at the vault root, because the vault
  is what discovery already resolves (CORE-13) — a command that found the bundles has
  therefore found the config, with no second search — and because a committed
  `okf/okf.json` reads as team contract in review the way a repo-root dotfile does not.
  Effective precedence as shipped: built-in defaults → global file → project file → CLI
  args. `OKF_HOME` is deliberately *not* a severity layer: it moves the personal vault
  (which bundles get linted), never a rule's severity.
- **`XDG_CONFIG_HOME` is honored for the global config**, falling back to
  `~/.config/okf/okf.json`. A widening of Q4, not a contradiction: the resolved location
  is the same on a stock machine.
- **CLI argument parsing is hand-rolled, not `System.CommandLine`.** The MVP surface is
  a verb plus a handful of flags; the exit-code contract (CLI-14) and the
  usage-versus-lint-failure distinction would have to override a framework's own error
  paths anyway; and the binary must publish NativeAOT-clean with nothing reflective on
  the path. Revisit if the command surface grows past what one `switch` reads well.
- **Q10 (NativeAOT) is answered by verification, not argument.** `dotnet publish -r
  linux-x64` with `PublishAot=true` succeeds with **zero trim/AOT warnings** and yields a
  **4.0 MB** self-contained binary that lints all four reference bundles in ~16 ms. The
  YAML choice (YamlDotNet through the representation model and event emitter only) holds
  up: no serializer, no reflection, no static-context source generator needed. The
  fallback to trim-safe self-contained non-AOT is therefore not required. `Okf.Cli` sets
  `PublishAot=true` in the csproj so the AOT analyzers run on *every* build and an
  AOT-hostile dependency fails the build rather than the release.
- **Q8 (near-duplicate heuristic) for MVP: title-or-filename collision after
  normalization** (lowercase, drop everything that is not a letter or digit), compared
  within a bundle, reported on the later concept in path order. Cheap, deterministic, and
  zero false positives on the four reference bundles. The vectorization spike replaces
  it later; the rule id (`OKF0303`) does not change when it does.
- **Diagnostic ids as shipped** (Q2's ranges, filled in):

  | Id | Nickname | Check | Default |
  | --- | --- | --- | --- |
  | `OKF0001` | unparseable-frontmatter | No frontmatter block, or one that does not parse (§11.1) | error |
  | `OKF0002` | missing-type | No non-empty `type` (§11.2) | error |
  | `OKF0003` | invalid-index-structure | `index.md` breaks §8 | error |
  | `OKF0004` | invalid-log-structure | `log.md` breaks §9 | error |
  | `OKF0101` | uncited-footnote | Footnote label joins to no `sources[].id` | warning |
  | `OKF0102` | unused-source-id | `sources[].id` is never cited | warning |
  | `OKF0103` | source-drift | `sources[].last_modified` > `generated.at` (CORE-8) | warning |
  | `OKF0201` | self-verification | A `verified[].by` equals `generated.by` | warning |
  | `OKF0202` | stale-concept | `today >= stale_after` (§5.5) | warning |
  | `OKF0301` | missing-description | No `description` | warning |
  | `OKF0302` | broken-internal-link | Bundle-internal link resolves to nothing (§6.1) | info |
  | `OKF0303` | near-duplicate-concept | Title or filename collision | warning |
  | `OKF0304` | missing-tags | No `tags` | hidden (opt-in) |
  | `OKF0305` | unregistered-tag | Tag absent from `lint.tagRegistry` | hidden (opt-in) |
  | `OKF0306` | generated-index-drift | A generated `index.md` differs from what `okf index` would emit | warning (added in the `okf index` milestone) |
  | `OKF0307` | missing-source-resource | A `sources[]` entry carries no `resource` (§5.1) | warning (added in the lint/search friction milestone) |
  | `OKF0308` | unresolvable-source-resource | A `sources[].resource` written as a path names nothing in the bundle (§6.2) | info (same) |
  | `OKF0309` | link-leaves-bundle | A markdown link resolves outside the bundle root (§6.2) | info (same) |

- **Deferred out of this milestone, deliberately.** (a) CLI-9's two rules —
  generated-file drift and `raw/` ingestion immutability — wait on `okf index` and the
  capture manifest respectively; neither has a rule id yet. (Generated-file drift got
  one, `OKF0306`, in the `okf index` milestone; `raw/` immutability is still waiting.) (b) CLI-7's "human actor on a
  CI commit" warning waits on Q9 (no reliable CI signal is decided). (c) Reserved-file
  structure checks are deliberately light: `index.md` is checked for the frontmatter rule
  (§8/§12), for at least one `#` heading when it has content, and for the
  `* [Title](link) - description` entry form; `log.md` is checked for ISO `##` date
  headings in newest-first order. Anything stricter risks erroring on a foreign bundle,
  which ACC-1 forbids. (d) A link that resolves *outside* the bundle root is not
  bundle-internal and is not reported at all. **(d) superseded by the lint/search
  friction milestone below: it is now reported as `OKF0309` at info.**

### Proposed decisions (pending review): lint review flags (2026-08-14)

- **Q1/Q8 reconciliation needed:** OKF0303's filename-collision arm fires on every
  pair of `<subdir>/about.md` files, but Q1 makes `about.md` the designated
  subdirectory-description carrier. Proposal: exempt `about.md` (and `index`-like
  reserved names) from the filename arm — to be fixed in the `okf index` milestone.
  **Implemented in the `okf index` milestone, widened to both arms.** A
  convention-bearing filename (`OkfBundle.IsConventionalFile`, currently just
  `about.md`) is exempt from the *title* arm as well as the filename arm, and such a
  file neither reports a collision nor seeds one for a later file. Rationale for the
  widening: the generic title that accompanies a conventional filename (`title: About`)
  is as much a convention as the name is, so leaving the title arm alone reproduces the
  same false positive one frontmatter key over. A conventional file's identity is its
  directory, not its name. `index.md`/`log.md` needed no exemption — §3.1 reserved
  names are never linted as concepts, so they never reach OKF0303.
- **OKF0003 entry-form strictness:** §8 doesn't say an index may contain only
  `[Title](link)` bullets; a foreign bundle with prose bullets would hard-error.
  Proposal: keep for our own generated indexes, but demote non-entry bullets to
  info for consumed bundles if a real foreign bundle ever trips it.
- **Scanner false positives (low):** footnote refs/links inside inline code spans or
  4-space-indented code blocks are still scanned; only fenced blocks are skipped.
  Accepted for now (info/warning severities only).
- **Footnote definitions count as citations:** a `sources[].id` that appears only in
  a footnote *definition* (never referenced in prose) escapes OKF0102. Accepted;
  revisit if it masks real drift. **Revisit trigger fired; reversed in the
  lint/search friction milestone below** — it masked real drift on the first
  bundle authored under the rule.
- **`okf version` prints the assembly default (1.0.0)** until CLI-17 release
  plumbing stamps the real version at publish time.

### Proposed decisions (pending review): the `okf index` milestone (2026-08-14)

- **Generated files are marked with an HTML comment,
  `<!-- generated by okf -->`, on its own line** — after the frontmatter on the
  bundle-root index, first line everywhere else. It is the mechanism the
  compatibility constraint needs: Google's reference bundles hand-style their indexes
  (bold titles, prose bullets, a `.py` file listed as an entry in
  `acme_retail/attesters/index.md`), and drift detection must not condemn them.
  Only a marked file can drift; an unmarked one is a foreign artifact okf-net reports
  on but never blames. An HTML comment rather than frontmatter because §8 permits
  frontmatter only on the bundle-root index and only `okf_version`; rather than a
  sidecar manifest because a bundle must stay complete as plain markdown (§1) and a
  manifest is one more file to lose; and rather than content hashing because deleting
  the marker is how a consumer *opts out*, which is a feature, not an evasion.
  **The position is part of the signal.** Detection reads the marker only where the
  renderer writes it — the first non-blank line, after a frontmatter block if there is
  one — rather than scanning the file for the string. Scanning would claim any
  hand-written index that merely *quotes* the marker (one documenting okf, one carrying
  the line in a fenced block), and claiming it has teeth: that index would be reported
  as drift and then overwritten as an "update" instead of as the replacement of
  hand-written content it is. Reading stays tolerant otherwise — CRLF endings, trailing
  whitespace, leading blank lines, and an edited root frontmatter block all still read
  as marked.
- **Ordering rule, as shipped.** Entries are grouped into `#` sections, one per concept
  `type` (missing/falsy `type` → `Other`), plus a `Subdirectories` section. Concept
  sections come **first**, ordered case-insensitively then ordinally by heading text;
  the subdirectory section is always **last**, so navigation follows content whatever
  the bundle's type vocabulary happens to sort like. Within a section, entries are
  ordered case-insensitively then ordinally **by link (i.e. by filename)**. Filename
  rather than title because every entry has one: `title` is optional (§4.1) and may
  repeat, so ordering by it is neither total nor stable, whereas a directory cannot
  hold two files of the same name. The ordinal tiebreak keeps the order total when two
  names differ only in case.
- **Deliberate divergences from `reference_agent`'s generator (ACC-3 enumerates, it does
  not forbid).** (a) Within-section ordering is by filename; the reference sorts by
  lowercased title. (b) The subdirectory section is pinned last; the reference sorts it
  alphabetically among the type headings — same result on every reference bundle today
  (`Reference` < `Subdirectories` in `stackoverflow/references/`), different the moment
  a type name sorts after it. (c) Subdirectory blurbs come from `about.md` or are
  omitted (Q1); the reference calls an LLM to synthesize them. (d) A subdirectory is
  listed **only when it gets an index of its own** — the reference lists every child
  directory, which is how `acme_retail/index.md` comes to link
  `attesters/index.md`, a file its own generator never writes. Listing a directory we
  do not index would emit a link to a file nothing produces. (e) The bundle-root index
  carries `okf_version: "0.2"` frontmatter (§12); no reference bundle's does.
- **Drift semantics.** Per directory, the on-disk `index.md` is compared byte-for-byte
  with the generated text and lands in one of five states: *created* (no file),
  *unchanged*, *drifted* (marked, differs), *foreign* (unmarked, differs), *orphaned*
  (marked, but the directory has nothing left to index). **Drift = drifted + orphaned**,
  and only drift fails `okf index --check`. Two consequences worth stating plainly:
  a *missing* `index.md` is not drift (nothing on disk can carry a marker, so okf-net
  cannot claim it wrote one), and a *foreign* index is not drift (ACC-1). The missing
  case is nearly always caught indirectly anyway — a new directory is a new entry in its
  parent's index, and that parent is marked — leaving only a deleted bundle-root index
  as a genuine blind spot. Accepted; revisit if it bites.
- **`OKF0306` (`generated-index-drift`), hygiene range, default `warning`.** The lint
  surface of PRD CLI-9's generated-drift rule. `okf lint` computes it from the same walk
  it already performs — the linter caches each file's text and parsed frontmatter and
  hands both to the generator — so surfacing it costs one in-memory render per directory
  and **no extra file read or YAML parse**. It is skipped entirely when configured to
  `hidden`. `okf init` (CLI-8) will promote it to **error** in the project config it
  writes, per decisions §7; the default stays `warning` because defaults block only what
  the spec says, and §11 does not require index files at all.
- **`--check` does not consult severity.** PRD CLI-10/CLI-14 fix `--check`'s exit code at
  1 on drift; that is the mechanical gate a hook or CI job branches on. Promoting or
  demoting `OKF0306` changes what `okf lint` reports, never what `--check` returns. Two
  surfaces, one drift computation.
- **A plain `okf index` run overwrites a foreign index, but says so.** PRD CORE-9 is
  explicit that generated index files are outputs and preserve nothing hand-written, and
  the user asked by naming the path. The run therefore replaces it and reports
  `replaced (was hand-written)` per file plus a distinct summary count, so the
  destructive case is never folded silently into "updated".
- **Orphans are reported, never deleted.** Removing a file a human may still want is not
  a generator's call, so an orphaned index fails `--check` until someone deletes it.
- **Deferred: CORE-9's "the root index's first entry links `about-this-bundle.md`".**
  Not expressible while entries are grouped by `type`, since that concept lands under
  its own type heading wherever the ordering puts it. Options when `okf init` lands
  (CLI-8): a pinned lead section, or dropping the requirement in favor of the
  `about.md`/`about-this-bundle.md` convention being discoverable by name. Left open
  deliberately — it is an `okf init` concern, not a generator one.

### Ruling: MS-PL is allowlisted (2026-08-14)

Ringo delegated the call. MS-PL stays in `scripts/licenses-allowed.json`:
OSI-approved permissive license, ASF Category A (may be included in Apache
products), and its one restriction (redistributed MS-PL *source* stays MS-PL)
cannot bite here — both MS-PL packages (Xunit.SkippableFact, Validation) are
test-only dependencies that never enter shipped binaries.

### Q7 resolution: search semantics (2026-08-14)

Ringo's ruling, recorded on work item #1 and mirrored at PRD §6 Q7.

**Option B: deterministic BM25-style lexical search.** Tokenized matching, not
substring; frontmatter weighted above body text; query terms AND-ed with an OR
fallback; path tie-breaks. Rejected alternatives: substring/`grep` matching (no
ranking, and a query for `metric` would hit `metrics` but not `Metric` unless the
match is case-folded anyway, at which point tokens are simpler), and shipping the
vectorization spike early (out of MVP scope by §1.3, needs a generated artifact
the bundle must not depend on, and is not deterministic in the way a hook and CI
need).

Four constraints come with the ruling, from the disclosure-versus-search
research:

- **Corpus = concept documents only.** Never `raw/` — it lives outside every
  bundle root by Q3, so bundle-scoped discovery cannot reach it, and this is
  asserted by test rather than assumed. Never the spec §3.1 reserved files
  (`index.md`, `log.md`): an index is a *view* of the concepts, so indexing it
  would double-count every title and description and rank navigation above
  content.
- **Scope = the same vault resolution `okf lint` performs** (CLI-1, CLI-3):
  project vault by walk-up, then the personal vault, and an explicit path
  overrides both. The determinism rule from topic 6 is the reason — a query must
  return the same results on every machine and in CI.
- **Links-first results.** Path, title, type, score, bounded snippet. Never a
  full body: search points *into* progressive disclosure and never replaces it.
- **Trust-aware fields.** Every result carries its trust tier (CORE-6) and stale
  flag (CORE-7) so an agent can judge a hit before spending a read on it.

**Filter syntax** is `tag:<x>` and `type:<y>`, inline in the query and repeatable,
with `--tag`/`--type` as the same filters spelled as flags. Comparison is
case-insensitive on the whole value, not on tokens, so `type:Reference` and
`type:reference` are one filter and `tag:cost-optimization` never matches a
concept tagged `cost`. Repetition folds by arity of the field: repeated `type:`
is **OR** (a concept has exactly one `type`, so AND-ing two would always return
nothing), repeated `tag:` is **AND** (a concept has many tags, so AND-ing narrows,
which is what a second filter is for). Filters restrict candidates *before*
scoring; they never contribute score.

**The JSON contract is engine-agnostic.** Nothing in a result record names BM25,
tokens, or fields — `score` is an opaque, higher-is-better number and `matchMode`
says only `all`/`any`/`filter`. The vectorization spike (§5, sqlite-vec; work
item #8 confirms the sqlite direction) can therefore be slotted behind the same
shape, and `okf mcp`'s `search` tool (MCP-2, MCP-3) can be written against it
today.

#### Scoring, as shipped

- **BM25 with `k1 = 1.2`, `b = 0.75`** (the standard defaults; named constants in
  `OkfSearchEngine`), over field-weighted term frequencies: title ×3, `tags` ×2,
  `type` ×2, `description` ×2, body ×1. `type` is weighted with `tags` because
  PRD CORE-11 requires `type` to be matchable and it is the same kind of
  categorical metadata; the Q7 ruling's "title/tags/description over body" is
  unchanged by where the categorical field sits.
- **IDF** is the non-negative variant, `ln(1 + (N − df + 0.5) / (df + 0.5))`, so a
  term present in most of the corpus can never subtract from a score.
- **Collection statistics (N, df, average length) are computed over the whole
  resolved corpus, not over the filtered candidate set.** A filter says which
  concepts may be *returned*; it does not change what the collection is, so
  `okf search widget tag:fixture` ranks the surviving concepts exactly as
  `okf search widget` does.
- **Deterministic by construction.** No clock (staleness takes an injected
  `today`), no network, no model call, and a total order on results: score
  descending, then bundle root, then bundle-relative path, both ordinal. Scores
  are rounded to four decimals on output so a formatting difference can never
  reorder or churn the JSON.
- **Tokenization: lowercase, Unicode-aware, split on anything that is not a
  letter or digit.** `Rune` enumeration, so surrogate pairs survive;
  `ToLowerInvariant`, which is full Unicode simple case folding even under the
  CLI's `InvariantGlobalization`. Hyphenated words split on both sides of the
  hyphen, in the query and in the corpus alike, so `sqlite-vec` is the two terms
  `sqlite` and `vec` and matches consistently.
- **No stemming, no stopword list, no minimum token length in v1 — deliberate.**
  A stemmer is per-language state a `cat`-readable format should not need, and
  the first thing that would have to be reproduced by any other implementation of
  the format's tooling (okf-gem, the reference agent). Field weighting recovers
  most of what stemming would buy on a corpus this size. Revisit with the
  vectorization spike, which is where fuzzy matching belongs.
- **Snippets** are a single ~160-character window of the body chosen to cover the
  most distinct matched terms, trimmed to word boundaries, whitespace collapsed to
  one line, elided with `…` at either end, with matched terms marked `**like
  this**` — markdown emphasis, because the payload is markdown and the same string
  has to read well in a terminal and in an MCP client. A frontmatter-only match
  snippets the `description` instead (then the head of the body, then nothing).
- **A concept whose frontmatter does not parse is not a corpus entry.** It is
  already an `OKF0001` error; `okf lint` is the surface that says so, and
  inventing a title-less, type-less result for it would put the same complaint in
  a second place.

#### Exit codes: PRD wins over the grep convention

`okf search` exits **0 when there are no results**, not 1. The grep convention (1
= no matches) was considered and rejected: PRD CLI-11 and the §3 CLI-surface table
both fix "0 (including no matches)", and CLI-14 reserves 1 for *diagnostics at
error severity*. An empty result set is not a diagnostic — a hook that ran
`okf search` to check whether a concept already exists (SKILL-2's search-before-
create) would otherwise fail the commit for the normal case. Exit 2 keeps its
usual meaning: usage or environment failure, including an empty query with no
filters.

#### Deferred out of this milestone, deliberately

- **Registry scope (CLI-3's opt-in).** `okf register` does not exist yet, so
  there is no registry to opt into and no `--scope`/`--all` flag is shipped.
  Search is project-scoped, full stop, until CLI-2 lands; the working-set
  resolution is already the shared one, so widening it is a change in
  `OkfDiscovery`, not in the engine.
- **`okf mcp`'s `search` tool.** The engine lives in `Okf.Core` and returns data,
  never formatted text, precisely so the MCP adapter is a rendering change; the
  server itself is the next milestone.
- **Phrase queries, negation, field-qualified free text** (`"exact phrase"`,
  `-term`, `title:foo`). None is needed by the capture skill's search-before-
  create loop, and each is a new grammar to freeze in the JSON contract.
- **An on-disk index.** Every search walks the resolved bundles and reads them.
  Four reference bundles is milliseconds; when it stops being, the generated
  artifact is the vectorization spike's, and the bundle must stay complete
  without it.

### Proposed decisions (pending review): the `okf mcp` milestone (2026-08-14)

- **Build vs buy: the protocol is hand-rolled, not taken from the official
  ModelContextProtocol C# SDK — and the SDK passed both mandated gates.** The
  evaluation was run, not argued: `ModelContextProtocol` 2.2.0 (and
  `ModelContextProtocol.Core` 2.2.0) were added to `Okf.Cli`, a probe server was
  written against each of the two documented shapes, and both gates were
  measured.
  - **Licenses: pass.** `mise run licenses` accepted the whole graph — both MCP
    packages are Apache-2.0, and everything they pull (`Microsoft.Extensions.AI.
    Abstractions`, the `Microsoft.Extensions.*` hosting/DI/logging stack) is MIT.
    52 components, all allowlisted, no new entry needed in
    `scripts/licenses-allowed.json`.
  - **NativeAOT: pass.** `dotnet publish -r linux-x64` with `PublishAot=true`
    produced **zero** trim/AOT warnings on both probes. The SDK carries its own
    AOT-compatibility test app, and the reflection-heavy registration paths
    (`WithToolsFromAssembly`, the non-generic `WithTools`) are the ones annotated
    `[RequiresUnreferencedCode]`; the generic `WithTools<T>()` path is clean.
  - **Rejected anyway, on three measurements the gates do not cover.** (a)
    **Size**: the binary goes from **4.2 MB to 11 MB** with `ModelContextProtocol.
    Core` alone and **13 MB** with the documented `Host.CreateApplicationBuilder`
    path (which additionally needs `Microsoft.Extensions.Hosting`, a package the
    getting-started sample does not mention). CLI-17's whole distribution story is
    a self-contained `curl | sh` binary; tripling it to adapt four JSON-RPC methods
    is the wrong trade. (b) **Lost responses on a fast client**: driven as
    `printf '…' | okf mcp`, the SDK's stdio server emitted **zero bytes** —
    reproducibly, three runs out of three — because stdin reached EOF while the
    requests were still in flight; holding stdin open for two seconds yielded all
    401 bytes of the same three responses. That is exactly the shape of a CI
    harness, a shell here-doc, and this milestone's own acceptance proof. (c)
    **stdout ownership**: the documented hosting configuration logs to *stdout*,
    the protocol channel, unless the sample's `LogToStandardErrorThreshold` line is
    copied; the single most safety-critical property of a stdio server is one its
    default gets wrong. (A fourth, minor: the SDK answers out of request order,
    which is legal but makes a transcript harder to read.)
  - **What we keep instead.** The surface is four methods — `initialize`,
    `tools/list`, `tools/call`, `ping` — over newline-delimited JSON-RPC 2.0, in
    ~300 lines with no new dependency, using the `System.Text.Json` reader/writer
    already on the AOT-clean path. It is the same call the CLI made about
    `System.CommandLine`, for the same reasons, and the boundary-layer precedent is
    `YamlBridge`. **Revisit when the surface grows past tools** — resources,
    prompts, sampling, elicitation, or a non-stdio transport each argue for the
    SDK, and none is in MVP scope (MCP-4).
  - **The SDK is still an oracle.** Its transcripts (from the evaluation) supply
    the expected wire shapes the protocol tests assert against, so the handshake is
    checked against an independent implementation rather than against ourselves.
- **Synchronous loop, one message at a time.** A line is read, handled, written,
  and flushed before the next is read. Nothing is ever in flight at EOF, which is
  the defect the SDK exhibits, and it makes the response order the request order.
  Concurrency would buy nothing: every tool is a filesystem read of a few
  milliseconds, and MCP-4 leaves nothing to overlap.
- **Framing is one JSON object per line, and the newline is part of the
  protocol.** Written explicitly rather than through the writer's platform line
  ending. Two writer configurations exist for a reason: protocol structures
  (`tools/list` result, the content envelope, the tool schemas) are emitted
  **compact**, because a raw newline inside a message splits it in two on the
  wire; payloads are emitted **indented**, because they travel as JSON strings
  where newlines are escaped and a model has to read them. A test pins it:
  five responses, five newlines in the whole stream.
- **Tools are `okf_list`, `okf_search`, `okf_read`** — MCP-2's `list`/`search`/
  `read` under a namespace prefix, because a client mixes servers in one flat tool
  list and a bare `search` there is a collision waiting to happen. The
  descriptions teach the doctrine rather than describing parameters: orient by
  disclosure (`okf_list`), retrieve by search (`okf_search`), open only what you
  picked (`okf_read`), never a body from search, and nothing from outside a bundle
  root. The same doctrine is repeated in the `initialize` result's `instructions`,
  for clients that surface it.
- **`okf_search` returns the Q7 array verbatim.** One writer (`SearchJson`) now
  renders both `okf search --json` and the tool's payload, so MCP-3's parity claim
  is about bytes rather than about fields, and is asserted as such — against the
  fixture bundle and against Google's reference bundles. Rendering it twice was
  the alternative, and it is how two surfaces drift apart one field at a time.
- **Two error channels, split by who has to recover.** A malformed argument —
  unknown tool, wrong JSON type, missing `path`, an empty query, a path that
  leaves the bundle root — is a **JSON-RPC error** (`-32602`), because the client
  is wrong and the model cannot fix it. A miss the model *can* act on — no such
  concept, no such bundle, an ambiguous path across bundles, frontmatter that does
  not parse — is a **tool result with `isError: true`**, because a protocol error
  is generally not shown to the model, and "search again" is precisely the next
  move. Traversal sits deliberately on the protocol side: MCP-5 wants a hard
  refusal, not a suggestion.
- **Scope is resolved per call, and validated once at startup.** Startup
  resolution failing is exit **2** (PRD §3's CLI-surface table) rather than a
  server that answers every call with the same failure; re-resolving per call means
  a bundle added to the vault while the server runs is visible to the next call,
  exactly as it would be to the next `okf search`. Nothing but JSON-RPC is ever
  written to stdout: `--verbose` resolution notes go to stderr.
- **No per-call `path` argument.** The server's scope is fixed by `okf mcp
  [path]`, the same argument every other command takes. A tool that accepted its
  own path would let a client widen the scope it was launched with, which is the
  containment rule (MCP-5) with a hole in it.
- **`okf_read` is CORE-12, implemented in `Okf.Core`** (`OkfConceptReader`), not
  in the adapter: it returns frontmatter, body, derived trust tier, and stale flag,
  and it is unit-tested without a process boundary. Containment is one shared
  primitive, `OkfBundle.TryResolve`, which refuses absolute paths (even ones that
  happen to point inside the bundle — otherwise the caller's path grammar depends
  on where the bundle sits on this machine), refuses `..` however it is spelled,
  and refuses a backslash outright rather than reinterpreting it as a separator on
  one platform and a filename character on another. The `.md` suffix is optional,
  so a search result's `id` and its `path` are both valid reads.
- **Frontmatter is projected to JSON without retyping.** Scalars travel as their
  source text — a date, a quoted `"0.2"`, a number-shaped string all come back as
  written — in source order, including keys okf-net does not model. Guessing JSON
  types would undo CORE-2 one field at a time. A YAML null becomes JSON null; a
  non-scalar mapping key (which JSON cannot express, and no bundle uses) is
  dropped.
- **`okf_list` has two modes, and both are progressive disclosure.** With no
  arguments it names the bundles in scope (with a concept count and the resolution
  sentence); with a bundle it returns that directory's listing — **the bundle's own
  `index.md` when it ships one, byte-for-byte, including a hand-written foreign
  one, and the CORE-10 synthesis when it does not**, with a `source` field saying
  which. Structured `entries` accompany the markdown either way, from the same
  generator `okf index` uses. Nothing is written: the synthesis is in memory, and
  an acceptance test snapshots the reference clone to prove a whole session leaves
  it byte-identical.
- **Deferred out of this milestone, deliberately.** (a) `structuredContent` and
  `outputSchema` on tool results — the payload is JSON text today; adding a
  declared output schema freezes more contract than MCP-2 asks for, and the Q7
  array is already the contract. (b) `resources/*` and `prompts/*` — not
  advertised, so they answer `-32601`; a bundle's concepts are reachable as tools
  and adding a second addressing scheme for the same files would double the
  surface. (c) Pagination (`nextCursor`) — `tools/list` is three tools. (d)
  Progress, cancellation, and logging notifications — every call is a
  millisecond-scale filesystem read. (e) Registry scope, for the same reason
  `okf search` defers it: `okf register` does not exist yet.

### Proposed decisions (pending review): the lint/search friction milestone (work item #21, 2026-08-14)

Five fixes taken straight from the dogfood friction log (work item #19, note_168).
Each is a place the tools fought the first real author working under them.

- **`OKF0102` now requires a footnote *reference*, not merely a definition — the
  earlier allowance's revisit trigger fired.** The lint-review flag above accepted
  definitions-count-as-citations and said "revisit if it masks real drift"; it masked
  real drift on the very first bundle authored under the rule
  (`toolset/vault-registry-and-config.md` declared `sources[].id: prd` and never cited
  it, and markdownlint's MD053 caught what `okf lint` did not — friction #3). A
  `[^id]: …` line is the note itself; the citation is the `[^id]` in the prose, which
  is what §5.1's "a stable key used to attribute individual claims" describes. Both
  occurrence forms still feed `OKF0101`: a definition for a label no source declares is
  as dangling as a reference to one.
  - **The scanner's definition test was wrong in a way the rule change exposed.**
    `MarkdownFootnote.IsDefinition` treated *any* footnote at column 0 as a definition,
    colon or not. Google's ga4 bundle cites sources with a bare `[^sample_queries]` on
    its own line under a SQL block, and reading that as a definition would have made
    seven genuinely-cited sources look uncited the moment OKF0102 tightened. The colon
    is now what makes a definition. Measured effect on the reference bundles: **two new
    warnings, both true positives** (`acme_retail`'s `revenue-policy`, declared and
    footnoted but never referenced, in `metrics/gross-margin.md` and
    `computations/gross-margin-period.md`); ga4, stackoverflow, and crypto_bitcoin are
    unchanged. ACC-1 is unaffected — warnings were always expected there.
- **`sources[].resource` is validated, in two rules, both in the hygiene range.**
  §5.1 makes `resource` REQUIRED within an entry, but §11 does not, so neither errors
  by default. `OKF0307` (**warning**) fires when an entry has no `resource` at all: a
  provenance record nobody can follow. `OKF0308` (**info**) fires when a `resource`
  written as a *path* names nothing inside the bundle — the same tolerance as broken
  links, for the same reason. The hygiene range rather than provenance `01xx`: `01xx`
  is the citation-integrity family (does the body's claim join to a recorded source),
  while these two ask whether a recorded pointer is usable at all, which is the
  "degrades usability" test the `03xx` range is defined by. The range is fixed by
  category and never reused, so this is the call that has to be made once.
  - **What counts as a path is deliberately narrow.** §5.1 explicitly allows a
    population or scope descriptor (`all queries in BigQuery project X`) and §6.2 an
    absolute URL, and neither is checkable offline (CLI-16 forbids the network anyway).
    A value is only resolved when it is unambiguously a path: no whitespace, no URI
    scheme, and either explicit path syntax (`/`, `./`, `../`), a `.md` suffix, or a
    directory segment followed by a name carrying an extension. SPEC §5.1's own
    `dashboards/exec-revenue` and a dotted `project.dataset.table` therefore read as
    descriptors. A missed typo costs less than a false info on a foreign bundle.
  - **Resolution is document-relative first, then bundle-root-relative.** §6.2 says a
    relative path is relative, and `LintText.TryResolveLink` resolves links that way —
    but three of Google's four reference bundles write root-relative source paths with
    no leading slash (`policies/margin-standard.md` cited from `metrics/`), and a
    pointer that names a real file in the bundle is doing its job whichever base the
    author had in mind. Reporting those four would have been the rule's entire yield on
    the reference bundles. A typo, or a path that leaves the root, still fails both
    bases. Net effect on all four reference bundles and the dogfood bundle: **zero
    diagnostics**.
- **`OKF0309` (`link-leaves-bundle`, info) reports a link that resolves outside the
  bundle root.** It was previously not bundle-internal and therefore silent, which is
  indistinguishable from correct (friction #4) — while a bundle whose links only work
  from inside this checkout is exactly what the portability claim in §1 rules out. Info,
  never error by default: §6.2 grants relative paths and says nothing about staying
  inside the root. This is a *report*, not a control: the MCP milestone's symlink and
  `..` containment already refuse to **read** outside a bundle root, and nothing here
  reads anything.
- **The lint summary says what ran; `--verbose` says it per rule.** `Checked 21 files
  in 1 bundle (18 rules: 16 active, 2 hidden): 0 errors, 0 warnings, 0 infos.` A clean
  run and a disabled one printed the same line before (friction #11), so a
  misconfiguration that silenced the gate read as a passing gate. `--verbose` now lists
  **every** rule with its effective severity and the layer that set it, not only the
  ones a config layer moved: an unexpected default is as surprising as an unexpected
  override.
  - **The JSON output does not gain a run-metadata envelope; stderr carries it.** PRD
    CLI-11/CLI-15 fix `--json` as a bare array, and MCP-3 pins the same shape for
    `okf_search`; wrapping it in `{ "run": …, "diagnostics": [...] }` would break every
    consumer that reads element 0 as a record, for a convenience. In `--verbose` the
    same counts are written to stderr (`okf: checked …`, `okf: 18 rules: …`,
    `okf: diagnostics …`), which is where `--verbose` already speaks and which is
    explicitly not part of the machine-readable contract. Revisit only if a consumer
    needs the metadata *without* a terminal, and then as a separate `--format` value
    rather than as a change to this one.
- **Search snippets flatten link syntax the way they already flatten emphasis.** A
  window landing inside a link target rendered `[The **custodian** model](**custodian**-model.md)`
  — half a link, with the match marked inside a URL (friction #12). Link text is prose
  and stays; the target goes, along with the brackets and parentheses, before the window
  is chosen, so the marker offsets and the surrogate-pair snapping all operate on the
  same flattened string as before. A whole-line link *reference definition*
  (`[label]: ../path.md`) is dropped: every character of it is address. A footnote
  definition is not — it carries prose. Only complete single-line link forms are
  rewritten, so `array[0]`, `[^src]`, and an unclosed bracket survive as written.
  - **Measured over all four reference bundles and the dogfood bundle** (≈450 queries,
    3200 snippets): every changed snippet came from a document containing markdown
    links, and **no snippet from a link-free document changed** — the byte-comparison
    the change was gated on.
- **Not addressed here, deliberately:** the fenced-code half of friction #12 (a snippet
  whose window lands in an ASCII diagram). Dropping fenced blocks from the snippet
  source is a corpus question, not a rendering one — the body is scored with the fences
  in it, so a match could then have no window to show — and it belongs with the
  scoring revisit, not with a markdown-flattening fix.

### Proposed decisions (pending review): the capture and custodian skills milestone (work item #3, 2026-08-14)

- **The skills ship as `skills/okf-capture/SKILL.md` and
  `skills/okf-custodian/SKILL.md`** — one directory per skill, frontmatter of `name`
  and `description` only, body in plain markdown. That is the agentskills shape Claude
  Code loads directly and a pi-style host can read without a shim, and it costs nothing
  to stay inside it. Nothing host-specific appears in either file: no tool names, no
  `allowed-tools`, no slash commands. What the skills instruct an agent to *do* is run
  `okf`, which is the layering decision (§4) restated where an agent will actually read
  it.
- **The capture manifest is a plain convention, not a CLI affordance.** Q3 made
  immutability a property tracked "via a capture manifest" and named no mechanism. The
  mechanism is `<vault>/okf/raw/manifest.json`, a JSON document the capture skill
  creates and the custodian skill closes; `skills/okf-capture/SKILL.md` carries the
  normative field list. Shape:

  ```json
  {
    "manifestVersion": 1,
    "captures": [
      {
        "id": "2026-08-14-okapi-bm25-paper",
        "form": "packet",
        "files": [{ "path": "…/original.pdf", "sha256": "9f2c…" }],
        "capturedAt": "2026-08-14T20:41:50-05:00",
        "capturedBy": "claude-fable/5",
        "originalUrl": "https://example.org/papers/okapi-bm25.pdf",
        "title": "Okapi at TREC-3",
        "sourceLastModified": "1994-11-01",
        "ingestion": {
          "at": "…", "by": "…",
          "concepts": ["bundles/<name>/references/okapi-bm25.md"]
        }
      }
    ]
  }
  ```

  `files[].path` is relative to `okf/raw/`; `ingestion.concepts` paths are relative to
  the vault root, because a capture may be ingested into more than one bundle. Entries
  are append-only and `ingestion` is `null` until ingested — which is also the custodian's
  work queue, so the manifest is the to-do list and the immutability record in one file.
  - **Why no command.** Every field is something the writing agent already holds
    (`date -Iseconds`, `sha256sum`, the URL it just fetched), so `okf capture` would be
    a JSON writer wearing a CLI. The MVP's remaining commands are the ones an agent
    *cannot* do by hand — ranking a corpus, rendering an index deterministically, walking
    a rule set — and adding a verb per convention is how a small binary becomes a
    framework. Revisit when the manifest gains a *reader*: `okf inbox` listing
    uningested captures alongside unacknowledged concepts is the shape that would earn
    it, and it lands with CLI-12 rather than before it.
  - **Why JSON, and why a sidecar rather than frontmatter.** `raw/` items are HTML, PDFs,
    and video — most cannot carry frontmatter at all, and the one metadata record has to
    cover the packet as a unit. JSON because the file is machine-maintained and never
    linted as a concept; it sits outside every bundle root, so it is invisible to `okf
    lint`, `okf index`, and `okf search` by construction (asserted by the Q7 corpus rule).
  - **`sha256` alongside git.** CLI-9 specifies git-based detection for the deferred
    immutability rule. The manifest records a hash anyway: it costs one `sha256sum` at
    capture time, it survives a vault that is not a git work tree (which CLI-9 concedes
    it cannot cover), and it lets an agent check an item without shelling out to git.
    The future rule may use either; the hash is the format-level record, git is the
    repository-level one.
- **CLI-9's `raw/`-immutability rule stays deferred, and the manifest does not make it
  trivial.** Three things stand between the manifest and `OKF03xx`: `OkfLinter` walks a
  *bundle root* and `raw/` is outside every one of them (a vault-scoped diagnostic is a
  new working-set concept, not a new rule); nothing in `Okf.Core` shells out today, so
  git-based detection is a new capability on an AOT-clean, offline-by-contract library
  (CLI-16); and a diagnostic needs a file and a line (CLI-15), which for this rule is a
  position inside `manifest.json` — a JSON document the linter has no reader for. Each is
  small; together they are a milestone, and they belong with `okf init` (CLI-8), which is
  what creates `raw/` and writes the config that promotes the rule. Until then
  immutability is skill discipline plus the recorded hash, which is what a convention
  buys.
- **The doctrine is stated in both skills, in the same words:** *orient by disclosure,
  retrieve by search, open what you pick; `raw/` is evidence, the bundle is knowledge.*
  It already lives in `okf mcp`'s tool descriptions and `initialize` instructions, and
  an agent that reads the skill and an agent that reads the tool list should come away
  with one model of the vault rather than two.
- **Written to be executed, not read.** Both skills follow the writing-for-agents levers
  Ringo supplied: every step ends on a checkable, environment-verified completion
  criterion (`okf lint` reports 0 errors and 0 warnings; every `sources[].id` has a
  `[^id]` reference in prose) rather than a fuzzy one; behaviour is phrased as the
  positive target, with exactly two prohibitions kept as guardrails, each paired with the
  positive it protects; and neither file caches `okf --help` — one exact command per key
  move, and `okf <command> --help` taught as the lookup for the rest. What the skills
  *do* cache is what the environment cannot confess: the capture-vs-cite test, the actor
  convention, the reason `raw/` sits outside a bundle root.
- **Validated by walking them, not by reading them.** Both procedures were executed
  end-to-end against a throwaway vault with the built CLI — orient, search-before-create,
  capture a blog post flat into `raw/` with a manifest entry, ingest it into a
  `references/` concept, cite it by footnote from a new concept, stamp `generated`,
  regenerate indexes, append to `log.md` — finishing at `0 errors, 0 warnings, 0 infos`
  and `0 drifted`. Every claim either skill makes about a diagnostic was then provoked
  on a copy and observed: `OKF0309` for a markdown link into `raw/` (which is why the
  custodian records the raw item by manifest id in prose instead), `OKF0201` for
  self-verification, `OKF0306` at error for a hand-edited index, `OKF0102` for a source
  declared and never referenced, `OKF0202` for a concept past its `stale_after`, and
  `OKF0004` for a `log.md` out of date order.
- **Reviewed by walking them again.** A second pass ran both procedures against a fresh
  vault taking every instruction literally, and its improvisation points became fixes:
  the custodian's ingest template declared a `sources` entry without asking for its
  `[^id]`, so a concept written exactly to it landed on `OKF0102` while that step's own
  completion criterion passed; `ingestion.concepts` (vault-root-relative) and
  `files[].path` (`raw/`-relative) sat side by side with only the second base written
  down; `okf verify` was asserted as available in the capture skill where the custodian
  hedges it as designed-not-built; and the custodian advertised stale concepts as a
  trigger while having no branch for one, though `OKF0202` is a default warning and so
  already blocked its publish gate. The last gap is the one worth recording as a rule
  rather than a repair: **a manifest that will not parse, or a `sha256` that no longer
  matches, is reported and left as found, never rewritten to make it parse** — an agent
  that repairs the immutability record is how the record is lost, and neither skill had
  said so.

### Proposed decisions (pending review): version stamping and tag pipelines (work item #10, 2026-08-14)

Work item #10 has two halves. This is the half that could be built now; the other —
flipping `main` to a release branch, adding `dev` as the prerelease channel and deleting
the `stable` placeholder — waits for an actual 1.0.0 and stays open.

- **`okf version` told the truth about nothing.** It printed `1.0.0`, which was the SDK's
  default `VersionPrefix` and not a number anyone had chosen. Nothing passed `-p:Version`,
  so a release-candidate binary and a laptop build were indistinguishable — and both
  claimed to be the 1.0.0 that does not exist yet.
- **The version flow is the SDK's own, with one property added.** `Directory.Build.props`
  sets a default and performs no arithmetic. Verified against the pinned SDK (10.0.400):
  `-p:Version=1.0.0-rc.14 -p:SourceRevisionId=abc1234` yields `AssemblyVersion 1.0.0.0`
  (the numeric core; the prerelease is dropped, which is all `AssemblyVersion` can hold)
  and `AssemblyInformationalVersion 1.0.0-rc.14+abc1234`. Writing our own version
  arithmetic would have reimplemented, worse, what the SDK already does correctly.
  - **The unstamped default is `0.0.0-dev`, not `1.0.0`.** A build nobody stamped should
    say so. The SDK's `1.0.0` is a lie a dev build tells with a straight face, and it
    becomes an ambiguous one the moment `main` flips to non-prerelease versioning and a
    real 1.0.0 exists.
- **Two version strings, deliberately.** `okf version` prints the full informational
  version including the `+<sha>` build metadata; MCP's `serverInfo.version` prints the
  bare semantic version. An rc tag can be rebuilt, so the version alone does not identify
  a binary and a bug report quoting it is one question short — but `serverInfo.version` is
  a value a client may parse or compare, and semver §10 excludes build metadata from
  precedence. One attribute, read two ways, for two audiences.
- **Git lives in the task, never in MSBuild.** `mise run publish-aot` derives the local
  version from `git describe --tags --always --dirty`; no target in this repo shells out
  to git, so a build from a source archive with no `.git` still works. `git describe`'s
  output is normalized rather than passed through, because two of its three shapes are not
  versions: a `-<n>-g<sha>` distance trailer is dropped and the commit it named is carried
  separately as build metadata from `git rev-parse` (where semver §10 puts a commit; the
  distance itself is not preserved), and a bare sha from an untagged tree becomes
  `0.0.0-dev`. Dropping the trailer is not cosmetic — left in the version it is *legal*
  semver that sorts **above** the tag it followed, because `rc.14-3-g1f334c9` compares as
  an alphanumeric identifier and alphanumerics outrank numerics. A build three commits
  past `v1.0.0-rc.14` would claim to be newer than `1.0.0-rc.15`.
  - **The SDK does query git, and that is fine.** Its built-in source-control support
    fills `SourceRevisionId` from the working tree when a `.git` is present, so a plain
    `dotnet build` already yields `0.0.0-dev+<40-char sha>`. Passing the property
    explicitly overrides it with the short sha the release actually names. The distinction
    that matters is that the *version* is never derived from the repo by the build — only
    the commit identifier is, and only when one is there to read.
- **Tag pipelines now have a job, so the "no jobs" wart is gone.** The `workflow:` rules
  have always allowed tag pipelines, but no job matched `$CI_COMMIT_TAG`, so every tag
  semantic-release cut started a pipeline that failed with "no jobs in this pipeline": a
  red pipeline as the *normal* outcome of a successful release, which is how a team learns
  to ignore red pipelines. The `publish` job AOT-publishes `linux-x64` stamped from the
  tag, uploads it to the generic package registry as `okf/<version>/okf-linux-x64`, and
  attaches an asset link to the release semantic-release already created.
  - **CI stamps from the tag; local stamps from `git describe`.** The `publish` job
    deliberately does not reuse the `publish-aot` mise task. That task *guesses* a version
    from the working tree because it has to; in CI the version is the tag, which is
    authoritative and needs no guessing. Both honour the same `-p:Version` /
    `-p:SourceRevisionId` contract, which is why there is one contract and not two.
  - **The package version is the tag without its `v`.** Probed empirically (2026-08-14):
    this instance accepts a deliberately non-semver generic package version, so the strip
    is not a validation workaround. It is so that the package version is
    character-for-character what the binary inside it answers to `okf version`.
  - **Asset linking is idempotent by asking, not by forcing.** Link names and URLs must be
    unique within a release, so a blind POST fails on any rerun or retried job. The job
    fetches the release and adds the link only when the package URL is not already on it.
    The alternative — POST and swallow the error — cannot tell "already linked" from
    "misconfigured", which is exactly the distinction a first release needs.
  - **The job waits for the release rather than racing it.** semantic-release creates the
    release from the main-branch pipeline that pushed the tag, so the two pipelines
    overlap. The AOT compile makes the release near-certain to exist by the time the link
    step runs, but "near-certain" is not a schedule; it retries, then fails loudly with the
    last response body, because a 404 accuses semantic-release and a 401 accuses the job's
    own token, and those are opposite repairs.
  - **The job refuses to publish a binary that disagrees with the tag.** It compares
    `okf version` from the freshly compiled binary against `<tag minus v>+<short sha>`
    before uploading anything. Printing the version and reading it by eye is not a check:
    a build that lost its stamp answers `0.0.0-dev`, which prints into a green log and gets
    uploaded under the tag's coordinates regardless. The one failure this whole change
    exists to prevent is a package whose name lies about its contents.
  - **Every artifact of a failed publish is kept.** The job's `artifacts:` block uses
    `when: always`, like `licenses` does for its SBOM: an artifact retained only on success
    is discarded in precisely the case it was retained for.
- **What is untestable until a real tag exists, and what was done instead.** No amount of
  local work exercises a tag pipeline. Mitigations: `glab ci lint` is green; the merged
  YAML was read back from the instance's `/ci/lint` endpoint to confirm that `extends`
  *replaced* `.dotnet`'s branch-only `rules` rather than appending to them; the release
  and asset-link endpoints were probed read-only against this project; and the generic
  upload URL was probed with a throwaway package, since deleted, which is what corrected
  the semver assumption above. Every step of the job echoes what it is about to do, URLs
  included, because on the first real tag the job log is the only debugger there is.
- **`curl | sh` is not built, and the README says so.** What the generic package registry
  buys immediately is a *stable, predictable* download URL, which is the load-bearing half
  of an installer. The README documents the download-and-chmod one-liner under an install
  section headed "prerelease binaries" that explains `rc` means what it says. The
  `PRIVATE-TOKEN` header in that one-liner is there only because the project is private
  today.
