# okf-net — Architecture Decisions (running log)

> **Status:** living document. This is the raw material for a future PRD.
> Captured from design sessions between Ringo and Claude (Claude Code), starting 2026-08-14.
> Reference implementation studied: Google's [knowledge-catalog](https://github.com/GoogleCloudPlatform/knowledge-catalog) repo (local clone at `~/code/knowledge-catalog`), especially `okf/SPEC.md` (OKF v0.2), `okf/src/reference_agent/`, `okf/bundles/acme_retail/`, and `toolbox/mdcode` + `toolbox/enrichment`.

## Vision

A personal + team knowledge system built on **OKF v0.2** (Open Knowledge Format: markdown + YAML frontmatter bundles, domain-first organization). Rejected pi-llm-wiki's epistemic-type folder layout (concepts/entities/syntheses/analyses) in favor of the spec's native position: directory structure follows the *domain*; document kind lives in frontmatter `type`; cross-cutting categorization via `tags`.

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

- **Deferred out of this milestone, deliberately.** (a) CLI-9's two rules —
  generated-file drift and `raw/` ingestion immutability — wait on `okf index` and the
  capture manifest respectively; neither has a rule id yet. (b) CLI-7's "human actor on a
  CI commit" warning waits on Q9 (no reliable CI signal is decided). (c) Reserved-file
  structure checks are deliberately light: `index.md` is checked for the frontmatter rule
  (§8/§12), for at least one `#` heading when it has content, and for the
  `* [Title](link) - description` entry form; `log.md` is checked for ISO `##` date
  headings in newest-first order. Anything stricter risks erroring on a foreign bundle,
  which ACC-1 forbids. (d) A link that resolves *outside* the bundle root is not
  bundle-internal and is not reported at all.

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
  revisit if it masks real drift.
- **`okf version` prints the assembly default (1.0.0)** until CLI-17 release
  plumbing stamps the real version at publish time.
