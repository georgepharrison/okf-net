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

- ~~**Bundler** — strip custodian machinery for distribution; SPIKE: how to package cross-bundle concept references (inline? vendor? dangling-link?).~~ **Both resolved (2026-08-15, work item #5)** — see [the bundler milestone](#proposed-decisions-pending-review-the-bundler-work-item-5-2026-08-15). The spike's answer is *leave the link dangling and record it*; the alternatives and why they lose are recorded there.
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

### Proposed decisions (pending review): custodian activation on this repo (work item #20, 2026-08-15)

Dogfood v2. The topic-1 custodian model stops being a paragraph and becomes a directory
that exists, on the only bundle we own. Everything below was taken while walking
`skills/okf-capture` and `skills/okf-custodian` literally, as their first real user.

- **`okf/custodian/` holds a recipe, a script, and no skills.** The layout §2 reserves
  is instantiated as `README.md` (what the custodian is and what runs where),
  `recipe.json` (the skills by repo path, the search seeds an enrichment pass starts
  from, the exact commands, the triggers), and `check-manifest.py`. The two skills are
  **referenced** as `skills/okf-capture/SKILL.md` and `skills/okf-custodian/SKILL.md`,
  never copied: §1 makes the shared toolset a versioned reference precisely so eight
  projects do not land on six versions of it, and okf-net is the degenerate case where
  the toolset and the vault are one checkout, so the reference is a path. `recipe.json`
  is JSONC like `okf.json` — nothing parses it today, and the comments are the half of a
  config file a reviewer actually reads.
- **The capture manifest is checked by a script, not by a rule, and that is the
  milestone's shape rather than a shortcut.** `OkfLinter` walks a *bundle root*;
  `okf/raw/` sits outside every one of them; so the manifest's parseability, its recorded
  hashes, and its ingestion pointers are invisible to every diagnostic the tool has. The
  capture-and-custodian-skills milestone already deferred CLI-9's `raw/`-immutability rule
  for three reasons that all still hold. `okf/custodian/check-manifest.py` covers the
  mechanically decidable part now, in Python 3 stdlib (§3: custodian scripts are Python so
  they run in arbitrary consumer and CI environments), read-only, offline, with `okf
  lint`'s exit-code convention. Its invariants: the manifest parses at `manifestVersion`
  1; every entry carries the six required keys, a unique `<YYYY-MM-DD>-<slug>` id opening
  on a day that exists, a `flat`/`packet` form, and ISO-8601-with-offset timestamps; every
  `files[].path` stays inside `raw/`, exists, and hashes to its recorded `sha256`; a `flat`
  capture is one file named `<id>.<ext>` and a `packet`'s files live under `<id>/`;
  **nothing sits in `raw/` unclaimed by an entry**, symbolic links included — a link is not
  the bytes that were retrieved, and a linked directory is a tree the walk cannot see into;
  and a closed `ingestion` carries `at`, `by`, and `concepts` paths that name files that
  exist, resolved from the *vault* root. It repairs nothing, on principle.
  - **What it deliberately does not check**: whether the concept is a good rendering of
    the artifact (the prose layer, which is the custodian's job and not a script's), and
    whether the artifact still matches its `originalUrl` (the network, which no gate here
    may touch — CLI-16).
  - **The rule this becomes.** When CLI-9 ships, this script is the specification for it:
    the invariants are the diagnostics, the vault-scoped working set is the new concept,
    and the `sha256` comparison is the detection the PRD currently describes as git-based.
- **`okf lint` stays a CI gate; the pre-commit hook gets only what is instant.** The hook
  runs `check-manifest.py` when something under `okf/raw/` is staged, and nothing
  otherwise. Putting `okf lint` in a hook would make every commit wait on a `dotnet run`,
  and ACC-6's requirement is that the path *can* run in a hook, not that it must.
- **`raw/` is committed, and the `.gitignore` question is settled by Q3 rather than by
  taste.** The bundler ships only `bundles/`, which is exactly what makes the originals a
  producer-side archive: this repository keeps what was retrieved, a consumer receives the
  `references/` concept extracted from it plus the original URL in frontmatter. Ignoring
  `raw/` would delete the only copy of the evidence the trust model rests on. Confirmed
  nothing in `.gitignore` matches it.
- **The first capture is Anthropic's "Equipping agents for the real world with Agent
  Skills".** It fails the capture-versus-cite test — a living vendor page with no version
  to pin, already carrying one dated in-place update — so the capture path is exercised
  honestly rather than by forcing a pinnable source through it. It also earns the slot:
  this document asserts the skills ship "in the agentskills shape" and cites nothing for
  what that shape is, and the article's core principle, progressive disclosure, is this
  project's own *orient by disclosure* pointed at a skill directory instead of a vault.
  Ingested to `references/agent-skills.md`, cited from `practices/custodian-model.md`.
- **`sources[].resource` for an in-bundle concept is written bundle-relative, with the
  leading slash.** `okf-capture` step 6's example writes `resource: references/okapi-bm25.md`
  from a concept in a sibling directory. §6.2 resolves a relative path *from the concept*,
  so that form names `<citing-dir>/references/…` and only survives because `LintText`
  falls back to the bundle root (the #21 leniency, added for Google's own bundles). A
  consumer implementing §6.2 literally gets a dangling pointer. `/references/agent-skills.md`
  is unambiguous under the spec's own second form. **The skill's example should be
  corrected**, since an example is what gets copied.
- **The bundle's tag vocabulary is closed, and the registry is a ratchet rather than a
  redesign.** `lint.tagRegistry` in `okf/okf.json` lists 56 tags, derived from what the
  bundle had already grown, grouped by facet in comments. The audit behind it: 54 distinct
  tags across 19 concepts, **41 used exactly once**, with visible clusters of one idea
  wearing three labels (`conformance`/`compatibility`/`interop`/`acceptance`;
  `conventions`/`layout`; `generation`/`determinism`/`drift`). Consolidation is worth
  doing and is deliberately not done here — merging tags rewrites frontmatter across every
  directory, which is a change to review on its own evidence rather than a rider on the
  one that first made the vocabulary visible.
  - **OKF0305 (unregistered-tag) → warning, not error.** An error on a one-day-old
    registry turns the first author reaching for an obviously right new word into a red
    pipeline. The warning still buys the entire governance benefit — the vocabulary can no
    longer grow by accident, because CI names any new tag the moment it appears. **The
    trigger for promoting to error is recorded rather than left to inertia**: after the
    registry has survived a consolidation pass and a few real additions.
  - **OKF0304 (missing-tags) → warning, in the same change.** A concept with no `tags` is
    trivially compliant with any registry; promoting only OKF0305 would make deleting the
    key the cheapest way to satisfy tag governance. The two rules are one decision.
  - **The registry's human face is a concept in the bundle**
    (`practices/tagging-discipline.md`, type `Playbook`): the facets and what each is for,
    the audit numbers, how a new tag gets added (in the same change as the concept that
    needs it, with the concept as the argument), and the
    tag-versus-directory-versus-`type` question that usually dissolves the urge for a new
    tag. A registry nobody can read is a spell-checker.
- **Activation is described honestly in the bundle, including what does not run.**
  `practices/custodian-model.md` and `about-this-bundle.md` now say the custodian is
  active *and* enumerate the absences: nothing scheduled, no staleness refresh, no
  verification (every concept remains `unverified` — an actor cannot verify its own work
  and no second actor has come through), and enrichment still human-initiated. A custodian
  that overstates itself is worse than none, and the staleness-refresh loop is its own
  milestone.
- **The bundle has a `log.md` for the first time.** §9's structure, newest first, opened
  by this milestone with an `Initialization` entry back-dated to the bundle's creation
  day. `okf lint` checks the date ordering (`OKF0004`), which is the only thing about the
  log that is mechanically checkable.

### Proposed decisions (pending review): mutation testing (work item #11, 2026-08-14)

Work item #11 asked for the systematic control behind AGENTS.md's "tests must be shown
to constrain the code" — the rule a reviewer had to enforce by hand after finding two
vacuous assertions during the ACC-1 run. **Stryker.NET 4.16.0** is that control.

- **It is a local `dotnet` tool, not a package reference.** `.config/dotnet-tools.json`
  now pins `dotnet-stryker` beside `cyclonedx`. Apache-2.0, verified from the `LICENSE`
  file inside the package (`dotnet-stryker.nuspec` declares `<license type="file">`, so
  there is no SPDX id to read anywhere) — the same license okf-net ships under.
- **Tool-manifest entries are invisible to the license gate, and that is now written
  down.** `mise run licenses` builds the SBOM from `Okf.sln`'s *package graph*;
  `dotnet-stryker` is not referenced by any project, so it does not appear in the SBOM
  at all — with it pinned in the manifest, `mise run licenses` still reports the same
  20 components, none of them Stryker. The gate
  therefore cannot enforce AGENTS.md's dependency rule for tools. **The rule still
  applies to them**: a tool that never enters a shipped binary is exactly the case the
  MS-PL ruling turned on, and the reason given there — test-only, never redistributed —
  is a reason to permit a *permissive* license, not a reason to skip the check.
  **So: check a tool's license by hand when adding it to the manifest, and record the
  finding in the commit.** Deliberately not automated: a second scanner over
  `.config/dotnet-tools.json` would be a second thing to maintain for a file that
  changes roughly once a milestone, and `dotnet tool install` already prints the package
  it resolved.

**Solution mode, one score.** `dotnet stryker` from the repo root discovers both
`Okf.Core` and `Okf.Cli` from `Okf.sln` and mutates them in a single pass, so there is
one number rather than two that have to be weighed against each other. `stryker-config.json`
carries everything except the output path, which Stryker has no config-file key for
(`output` is CLI-only) — hence the `--output artifacts/stryker` in the mise tasks and
the CI job, pointing at the directory `.gitignore` already covers.

**No exclusions.** `mutate` is left at its default (`**/*`). There is no generated code
in `src/`, and the two categories that might have tempted one — `Program.cs` and the
argument parsers — are exactly where an untested branch would hurt a CLI most.

**The baseline, measured on the tip of `main` (1f334c9), 8 cores, 7m27s:**

| Project  | Score  | Killed | Survived | Timeout | No coverage | Ignored | Compile error | Total |
| -------- | ------ | ------ | -------- | ------- | ----------- | ------- | ------------- | ----- |
| Okf.Core | 70.49% | 907    | 313      | 8       | 70          | 294     | 501           | 2093  |
| Okf.Cli  | 71.22% | 635    | 190      | 1       | 67          | 149     | 278           | 1320  |
| Solution | 70.79% | 1542   | 503      | 9       | 137         | 443     | 779           | 3413  |

The two projects landing within 0.8 points of each other is the useful part: there is no
weak half to attack, and 70.79% is an ordinary first mutation score for a suite written
without one.

**Thresholds: `break` 60, `low` 70, `high` 85 — each chosen against that baseline.**

- `break: 60` is the only number with teeth (below it Stryker exits non-zero). Set ~11
  points under the measured 70.79% so that ordinary churn — a refactor that adds
  branches ahead of the tests for them — cannot turn a scheduled run red for a reason
  nobody would act on. A gate that flakes gets disabled, and a disabled gate is worse
  than a low one.
- `low: 70` sits *at* the baseline on purpose. It colors the report the moment the suite
  regresses below where it stands today, which is the signal actually worth having, and
  it costs nothing when it fires because it does not affect the exit code.
- `high: 85` is the target, not a claim. It is where the survivor analysis says the
  suite could get without writing tests for equivalent mutants.

**`mise run mutate` and `mise run mutate-quick`.** The second uses Stryker's `--since`
against `origin/main`, and it needed one thing to be useful at all. Stryker's diff filter
is conservative: a change to *any* non-C# file in scope makes it abandon the filter and
test every mutant ("Non-CSharp files in test project were changed"). Measured on this
branch, which touches `mise.toml`, `.gitlab-ci.yml` and several markdown files, `--since`
first ran the full 2055 mutants in 6m51s — it saved nothing. `since.ignore-changes-in`
in stryker-config.json fixes it by naming the paths that cannot affect a test (docs, the
dogfood bundle, the skills, CI and mise config, the tool manifest, this file); the same
branch then narrows to 1210 mutants for the right reason, "One or more covering tests
changed". **`tests/**/fixtures/**` is deliberately not on that list** — fixture bundles
are test inputs, and ignoring a change to one would be a wrong answer rather than a fast
one. Two sharp edges found the hard way: the patterns need a `**/` prefix (anchored forms
like `docs/**` silently match nothing), and a `since` block in the config file *enables*
the feature, so `since.enabled: false` has to be written explicitly or `mise run mutate`
tries to diff against Stryker's default target `master` and aborts. Even so, a `--since` score is not comparable to a full run (mutants outside the
diff are reported with no result): read it as "did my change arrive with tests that kill
its mutants", never as the project's score.

**CI: scheduled or manual, never a push gate.** A `mutation` job in `validate` runs on
`$CI_PIPELINE_SOURCE == "schedule"` (blocking, on `break`) and is `when: manual` +
`allow_failure: true` on a branch push. The manual variant needs `allow_failure`
explicitly: a manual job blocks its stage by default, which is the opposite of the
intent. `schedule` is added to the top-level `workflow:` rules — after the
`merge_request_event: never` rule, so MR pipelines stay off — and the `release` job gains
a `schedule: never` rule of its own, because a scheduled pipeline runs on a branch and
would otherwise reach semantic-release on `main` on a timer. Artifacts: the HTML, JSON
and markdown reports, 30 days, `when: always`, on the same reasoning as the SBOM.

**The survivors, classified.** 503 survived. The full classification is in the work
item; the shape of it:

- **Genuine gaps worth fixing now (fixed).** Six sites, nine mutants, killed with six
  new tests — chosen for being contracts rather than details: `HasDrift` as *any* rather
  than *all* index (`Indexes.Any` → `Indexes.All` survived because no test ever had a
  drifted index next to a clean one); the `okf index --check` hint that must stay off
  for an orphan; two boundary conditions where equality is not drift (a log entry on the
  same date as the one before it, a source last modified on the generation date); the
  layer name reported for `treatAllWarningsAsErrors`, which the old assertion checked
  only by substring; and BM25's length normalization, which nothing constrained — nine
  mutants survived in `Score`, seven of them arithmetic, and the new test kills three of
  those (`K1 *` → `K1 /`, `B * concept.Length` → `B / concept.Length`, and the division by
  `frequency + normalization` → multiplication).
- **Equivalent or near-equivalent (documented, not chased).** The clearest specimen:
  `Array.IndexOf(SupportedProtocolVersions, requested) >= 0` → `> 0` in the MCP
  handshake. The two differ only when the index is 0 — i.e. when the client asks for
  the latest revision — and in that case both branches yield `LatestProtocolVersion`.
  No test can distinguish them. Same family: the `corpus.Count > 0 && total > 0`
  divide-by-zero guard in BM25 statistics, and the BM25 constants whose mutants scale
  every score identically and so cannot change a ranking — `(K1 + 1)` → `(K1 - 1)` and
  the `*` before it → `/` are both a constant factor on every term. Four of the six
  mutants still alive in `Score` are this: those two, plus `frequency <= 0` → `< 0` and
  the `continue` → `;` beside it, which between them only let a term with zero frequency
  add a zero.
- **Live and killable, recorded rather than chased: the other two in `Score`.**
  `1 - B` → `1 + B` and `concept.Length / statistics.AverageLength` →
  `concept.Length * statistics.AverageLength` both survive the new test, because both
  keep the score *monotone* in document length and the test pins an ordering. Separating
  them needs a fixture trading term frequency against length, where the true formula and
  each mutant disagree by well under a percent of the score — numerology that would read
  as a change-detector rather than as the contract the ordering test states. So: BM25's
  length normalization now has a direction nothing can silently reverse, not a shape.
- **String-literal mutants on diagnostic prose (136 survived, the largest single
  group).** These are only sometimes gaps. Where a message is a *contract* — OKF0102's
  "a definition is not a citation" — it is already pinned. Where it is wording, pinning
  it verbatim buys a change-detector test, which is the vacuity this whole exercise is
  against. Not chased on purpose.
- **Dead-or-defensive code candidates — recorded for work item #13, deleted here:
  nothing.** 137 mutants have no test coverage at all, concentrated in
  `IndexArguments.cs` (9), `IndexCommand.cs` (15), `OkfLinter.cs` (13), `OkfValue.cs`
  (11) and `McpToolset.cs` (9), and `Program.cs` is 0% with its single mutant
  uncovered. Uncovered is not the same as dead, and mutation testing cannot tell them
  apart — that is #13's job. The no-coverage list is the input it should start from.

**Where the branch leaves it.** Re-running the full suite after the six new tests:
Okf.Core 71.19%, Okf.Cli 71.44%, solution **71.29%** (1553 killed, 493 survived, 9
timeout, 136 no-coverage), 6m58s. The nine mutants aimed at are dead, plus one more the
BM25 test caught for free. Half a point for six tests is the honest exchange rate, and it
is why the follow-up is a survivor-reading habit rather than a campaign.

**Not adopted:** the Stryker dashboard and `--with-baseline` (both want hosted storage;
the artifact is enough), and any per-push gate.

### Proposed decisions (pending review): `okf init` (work item #4, 2026-08-15)

CLI-8 and the half of CLI-9 that was waiting for it. Everything below was decided
against the vault this repository built by hand first, deliberately, so the conventions
could be argued with while they were still cheap to change (work items #19 and #20).

- **The canonical timestamp form is RFC 3339 UTC, `Z`-suffixed, second precision** —
  `2026-08-15T14:00:00Z`. Friction #19-9 and #20-12 found three spellings inside one
  repository, all of them legal ISO 8601, none of them preferred anywhere: Google's
  reference bundles stamp `Z`, this bundle stamped the local offset `date -Iseconds`
  handed the author, and the capture manifest stamped a third at `capturedAt` and
  `ingestion.at`. The pick is `Z` because it matches the reference bundles, because a
  `Z`-suffixed second-precision stamp sorts lexicographically in the same order it sorts
  chronologically — which every diff, index, and log listing quietly relies on, and which
  a local offset does not have — and because an offset records where the author was
  sitting, which is not information the format asked for. Second precision because a
  knowledge concept is not a high-frequency event and subsecond digits are noise in a
  reviewed diff. `OkfTimestamp` is the one place that renders it; `okf init` writes
  `generated.at` through it, and CORE-14 stamping does too — `okf verify` renders every
  `verified[].at` through the same helper rather than through a second formatter.
  - **Reading stays tolerant, and nothing is rewritten.** No rule rejects another
    spelling, and the stamps already in the bundle are left alone: restamping nineteen
    concepts is a migration to review on its own evidence, not a rider on the change that
    first named the form. Three concepts this milestone edited anyway were restamped into
    it, which is the ratchet, not the migration.
- **`okf.json` is JSONC, and that is now written down in three places.** Friction #19-10
  and #20-13: the file is named `.json`, is parsed with comments and trailing commas
  allowed, and nothing said so outside `OkfConfig.cs`. The convention is kept rather than
  renamed to `okf.jsonc`, because the reason for it is the point — a severity promotion
  without a stated reason is a promotion nobody can review, and the reasons are the half
  of a configuration file a reader actually needs. The cost is accepted rather than
  hidden: a strict JSON editor or schema will flag a file the tool reads happily. It is
  stated in the header comment of the `okf.json` `okf init` writes, in the bundle's
  vaults-and-config concept, and here. `recipe.json` follows the same convention for the
  same reason.
- **`okf init` writes ten files and never overwrites one.** The layout is §2's:
  `README.md` and `okf.json` and `.markdownlint.yaml` at the vault root, `custodian/`
  with a README and a recipe, `raw/` with `.gitignore` and an empty `manifest.json`, and
  `bundles/<name>/` with `about-this-bundle.md`, `log.md`, and a generated `index.md`.
  Every file is written only when it is missing, so a second run reports what is there,
  changes nothing, and exits 0 — which is what makes the command safe in a setup script.
  Nothing merges, patches, or repairs a file somebody already owns.
  - **The index is generated, not templated.** `OkfIndexGenerator` writes it, so a
    scaffolded index is byte-identical to what `okf index` would write a second later.
    A templated one would drift the first time the renderer changed, and the config init
    writes promotes `OKF0306` to error in the same breath.
  - **Refusals exit 2 and write nothing**: a vault at or inside a bundle root (the README
    trap, #19-14, named at the moment it can still be avoided rather than later as a
    generic `OKF0001` on a committed file); a `--name` that is not a single usable
    directory name; `--personal` beside a path, because the personal vault's location
    comes from `OKF_HOME` or `~/okf` and nowhere else.
  - **A directory that already holds `bundles/` is the vault**, read exactly as
    `OkfDiscovery` reads it, so `okf init okf/` fills an initialized vault in rather than
    nesting a second one at `okf/okf/`.
  - **`--personal`'s bundle is named `personal`.** A project vault's bundle takes the
    project directory's name; the personal vault's parent is the home directory, so
    deriving one there would stamp the machine's account name on the bundle.
- **The scaffolded `tagRegistry` is present and seeded, not empty.** Friction #20-8 asked
  for "empty-but-present", and the intent — the vocabulary is closed from the first
  commit, so it can never grow by accident — is met. The letter is not, deliberately: an
  empty registry with `OKF0305` at warning makes a freshly initialized vault emit a
  warning for every tag on the one concept `init` itself wrote, and a command whose output
  does not pass its own gate teaches the wrong first lesson. The registry therefore holds
  exactly the three tags `about-this-bundle.md` carries, which is also the discipline the
  registry documents: a tag arrives in the same change as the concept that needed the
  word. `OKF0304` and `OKF0305` both sit at warning, as one decision — a concept with no
  tags is trivially compliant with any registry, so promoting only `OKF0305` would make
  deleting the key the cheapest way to satisfy tag governance.
- **`raw/.gitignore` un-ignores everything, directories first.** Friction #20-9. The
  danger is not a deliberate rule but an inherited one: an ordinary repository's `*.log`,
  `*.tmp`, `tmp/` or `*.pdf` patterns match real captured artifacts, and a capture git
  silently declines to stage looks exactly like a capture that worked, right up until
  someone clones the repository and the manifest points at nothing. `!*/` precedes `!*`
  because git cannot re-include a file while a parent directory of it is still excluded.
  The file is vault machinery rather than evidence, so `check-manifest.py`'s
  "nothing sits in `raw/` unclaimed" check exempts it alongside `manifest.json`.
- **The vault `.markdownlint.yaml` carries `extends` only when there is something to
  extend.** Friction #19-5 and #19-6: MD025 is structurally unsatisfiable for OKF markdown
  and has to be turned off, and a nested config *replaces* the parent rather than merging
  with it, so a vault config written without `extends` silently switches the host repo's
  disabled rules back on. Emitting `extends` unconditionally is worse than not emitting
  it: `extends` naming a file that is not there is an error, which would break markdownlint
  for a repository that had no config at all. init looks for the four names markdownlint
  reads and writes the line only on a hit.
- **CLI-9's `raw/`-immutability rule ships as `OKF0310` (raw-item-mutated), warning by
  default, and the linter learns a vault scope for it alone.** The three obstacles the
  capture-and-custodian milestone listed are addressed rather than removed: `OkfLinter`
  still walks bundle roots, and `OkfLintOptions.VaultRoot` adds one vault-scoped pass
  beside them; `Okf.Core` still shells out to nothing, because detection is the manifest's
  recorded `sha256` rather than git; and the diagnostic's position is found by locating
  the recorded hash in the manifest text, which needs no JSON reader that reports
  positions.
  - **Detection is by hash, superseding the PRD's "git-based".** The hash is the
    format-level record, it works in a vault that is not a work tree — which CLI-9
    conceded git cannot cover — and it needs no process launched from a library that is
    offline and AOT-clean by contract (CLI-16).
  - **Only closed entries are judged.** An entry whose `ingestion` is still `null` is the
    custodian's work queue; re-capturing a page before anything cited it is ordinary work,
    not a broken record. Immutability starts at ingestion, which is what Q3 says.
  - **The rule is narrow, and `check-manifest.py` stays.** The script specified these
    invariants first and remains the belt-and-braces gate beside `okf lint`: the id
    grammar, the timestamp forms, the flat/packet layout, unclaimed files in `raw/`, and
    ingestion pointers resolving to concepts that exist are all still its findings. A
    manifest that will not parse is therefore *silent* in the rule rather than reported —
    the script reports it, and a rule that cannot read the record cannot claim the
    artifact changed. Neither ever repairs anything.
  - **A bundle with no vault around it leaves the rule inapplicable**, which is CLI-9's
    own concession, restated in the scope that turned out to matter. A bundle *inside* a
    vault does not opt out: it resolves the same vault whose `okf.json` already decides
    severities.
- **The dogfood vault is pinned to `okf init` by a test, and the test found the one real
  deviation.** ACC-5's vault was hand-built before the command existed, which makes it the
  specification the command has to reproduce; a test now scaffolds a vault into a temp
  directory and compares the file set, the first concept's frontmatter keys, the
  bundle-root index's `okf_version` and generated marker, and the config's promotions
  against `okf/`. It failed on the first run because `okf/raw/.gitignore` did not exist —
  the friction that asked for one was recorded on a milestone that could not yet act on
  it — and the fix went on the dogfood side. Two differences remain and are asserted
  rather than papered over: `okf/` holds far more than init writes, and its
  `about-this-bundle.md` carries a `sources` key a bundle scaffolded five seconds ago
  cannot have. The config comparison is one-directional for the same reason — every rule
  init promotes must be promoted *at least as far* in `okf/`, not identically.

### Proposed decisions (pending review): the staleness-refresh and acknowledgment loop (work item #7, 2026-08-15)

The last of the five steps of "surface, don't land" that was still prose. `okf inbox`
and `okf verify` ship, the custodian skill gains the branch friction #20 named as
missing ("clearing stale" had no procedure), and a scheduled CI job publishes the
inbox. What does **not** ship is any automation that refreshes content: PRD §1.3 makes
that a non-goal, and it stays one.

- **`okf inbox` reports per concept, not per finding, and that is the difference between
  it and `okf lint`.** The two commands look at the same three signals — acknowledgment
  (CORE-15), staleness (`OKF0202`), source drift (`OKF0203`) — and answer different
  questions with them. Lint asks whether a bundle is in good order, so a concept that is
  expired *and* citing two moved sources is three diagnostics at three severities. The
  inbox asks what a person should look at next, so the same concept is one row carrying
  three reasons. Merging them would have meant either a lint rule that reports
  acknowledgment (a severity on "nobody has read this yet", which is not a defect) or an
  inbox that speaks in rule ids (an engine detail in a contract that must outlive the
  engine).
  - **Exit 0 always, with `--fail-if-any` for the caller that wants the branch.** An
    inbox that turns a pipeline red is an inbox people learn to route around, and on a
    vault written entirely by agents it would be red on day one and every day after. The
    flag exists because "is anything waiting" is a legitimate question for a script to
    ask; it is off by default because the answer is a report.
  - **The third arm of CORE-15 is written down rather than left implicit.** The PRD's
    definition — `generated.at` newer than the latest `verified[].at`, or `status:
    draft` — says nothing about a concept nobody has verified at all, because there is no
    `verified[].at` for the comparison to fail against. Unacknowledged is the right answer
    for agent-written content and the wrong one for a concept a **person** generated, so
    the rule is: no verification events at all, behind a `generated` stamp whose `by` is
    not a `human:` actor. An absent `by` counts as non-human — what a concept does not
    record, it cannot claim.
  - **Verified-but-undatable is acknowledged.** A verification event carrying no readable
    `at` cannot be shown to predate the content, and reporting against a timestamp okf
    invented would be worse than saying nothing. A malformed `at` never wins the
    latest-verification comparison either, so it cannot mask a good one.
- **Timestamps compare at the coarser of their two precisions, and the drift test takes
  the union of that ordering and the linter's.** `OkfLifecycleInstant` carries both the
  date as written and the instant, and `Compare` uses instants only when both sides have a
  time.
  That is what lets two writes on the same day be ordered at all — the case the linter's
  `text[..10]` truncation cannot see. It deliberately does *not* order a bare date against
  a same-day instant: `last_modified: 2026-08-15` against a concept generated
  `2026-08-15T01:41:50Z` reads as the same day, because a date-only value means *some time
  that day*, and choosing midnight or the end of the day would be choosing the answer
  rather than reading it. The date compared is the one **written**, not the UTC one: an
  instant written `2026-08-14T20:41:50-05:00` is the 14th to its author and the 15th in
  UTC, and the author's day is what the document means.
  - **Which leaves one case where the two orderings disagree, and drift takes both.**
    `2026-08-15T01:00:00+05:00` is a *later written date* than `2026-08-14T23:00:00Z` and
    an *earlier instant*. `OKF0203` compares written dates, so it reports that source as
    drifted; an instants-only comparison does not, and the inbox would then be quieter
    than the linter about the same file — the one hole a single surface for "what needs a
    person" must not have. So `Compare` stays a strict order, because the latest
    verification is picked with it, and drift asks the wider question
    (`IsAfterAtEitherPrecision`): later by instant **or** later by written date. Wider,
    never weaker — equal stays equal, and an earlier value stays earlier.
- **`okf verify` edits the file's text, not a re-emitted document, and this is a
  correctness decision rather than an optimization.** A round trip through YamlDotNet is
  faithful in CORE-2's sense — same keys, same order, same values — and is not
  byte-faithful: it re-indents block sequences to column 0, closes up `{ a: b }` to
  `{a: b}`, and single-quotes a timestamp sitting inside a flow mapping (a colon is an
  indicator in flow context). Every concept in this vault carries
  `generated: { by: …, at: … }`, so the first `okf verify` would have arrived as a diff
  touching every line of every frontmatter it stamped, which is the opposite of what an
  acknowledgment is. `OkfStamp.VerifyText` inserts lines instead, and reaches the three
  shapes that occur: no `verified` key, a block sequence, and §5.2's bare mapping on one
  line. Anything else — a block-style bare mapping, duplicate keys — falls back to the
  emitter, because re-indenting somebody else's frontmatter to save a fallback is a worse
  trade than a noisy diff on a shape nobody writes.
  - **The insertion is checked before it is returned.** The result is parsed back and
    required to hold exactly one more verification event, ending with the actor just
    written. Text surgery that produced something okf cannot read is surgery that did not
    happen, and the emitter takes over.
  - **The actor is validated as a §7 form and rejected for quotes, backslashes, and
    control characters.** It is written into a double-quoted YAML scalar, so without that
    check `--by` is a way to write arbitrary frontmatter into somebody's concept.
- **Q6's fallback reads git through an environment the caller controls.** The chain is
  `verify.actor` (project config, then global), then `git config --global user.email`,
  then a refusal naming both. The git read passes `HOME`, `XDG_CONFIG_HOME`, and the
  `GIT_CONFIG_*` variables through from `OkfEnvironment` — which a child process would
  inherit anyway, so the point is that a caller can **override** them. That is what makes
  the Q6 behaviour testable against real git rather than a stub: the test writes a global
  config, sets a *different* repository-local `user.email`, and asserts the global one is
  what gets stamped. Asserting Q6 against a fake would have been asserting the fake.
  - **A configured `verify.actor` that is not a person is a configuration error, not a
    coerced `human:` stamp.** `process:nightly` or `claude-fable/5` in `verify.actor`
    means someone wanted a machine confirmation; `--by` is that surface, and silently
    prefixing `human:` onto either would manufacture the one signal §5.3 exists to carry.
  - **Self-verification is refused with exit 2 rather than warned about.** `OKF0201`
    reports it after the fact because a bundle may arrive containing one; the command
    whose entire product is that signal must not create one. Every named concept is read
    and checked before the first byte is written, so a run that refuses the third leaves
    the first two unstamped — a half-applied acknowledgment is worse than none.
  - **`okf verify` clears acknowledgment and nothing else, including on a draft.** A
    concept carrying `status: draft` stays on the inbox after it is verified, until a
    person removes the marker. That asymmetry is deliberate and is written into both the
    skill and the bundle: landing a draft is two acts, and collapsing them into one would
    make `status: draft` unable to survive the verification it was waiting for.
- **The custodian's refresh procedure is prose in the skill, and there is no
  `refresh-report.py` beside it.** `okf inbox --format json` *is* the machine interface;
  a Python script re-deriving the same rows would be a second implementation of CORE-15 to
  keep in step with, and the part that cannot be scripted — fetching what changed,
  judging what it means for the concepts that cite it — is exactly the prose layer the
  custodian exists for. The procedure's four steps carry checkable Done-when bounds, the
  third of which is the one that matters: after restamping, `okf inbox` still lists the
  concept, under **Unacknowledged** and nothing else.
  - **`stale_after` moves out in the draft only.** It is the sentence "somebody re-checked
    this today", which is false until a person lands the draft — so the draft is where it
    is written and the merge request is what makes it true.
  - **`verified` survives a refresh that contradicts it.** A past human verification is a
    record of what a person read, not a claim about the current text; the custodian says
    so in its reply rather than deleting the record.
- **The scheduled `custodian-inbox` job publishes and never gates.** Schedule-only, like
  `mutation`, because the inbox answers a question about elapsed time and running it per
  push would re-report the same rows all day. It runs both forms — the text report so the
  score is readable in the job log without downloading anything, the JSON as a 30-day
  artifact so the refresh procedure has something to start from — and it deliberately does
  **not** pass `--fail-if-any`. With no schedule configured on the project the job never
  runs, which is a visible absence rather than a silent one.
- **Q11 and Q12 are answered in passing, and the answers are recorded here rather than
  left to the code.** Q11 (scope granularity): both commands take the working set every
  other command takes — `okf inbox` resolves a path exactly as `okf lint` and `okf search`
  do, and `okf verify` takes concept paths, one or many, because a stamp names a document
  a person read. Neither reaches the registry, for the same reason `okf search` does not:
  `okf register` does not exist. Q12 (machine verification surface): `--by` on
  `okf verify`, not a separate command — the refusals it needs (a §7 actor, never the
  generating one) are the ones `okf verify` already performs, and a second command would
  be the same code behind a different name.
- **Reading a timestamp and writing one are two types, and only one of them is named for
  a form.** #4 and #7 each landed a `Okf.Core.OkfTimestamp` in the same namespace,
  independently and both correct: #4's is the canonical write form (`ToCanonical`,
  `IsCanonical`), #7's is the comparison this section describes. Reconciling them kept
  `OkfTimestamp` for the write form — it is the merged, public, referenced-from-the-skills
  one — and renamed the comparison to **`OkfLifecycleInstant`**. The name is not cosmetic:
  the write form has exactly one spelling by choice, while what this type reads may be a
  bare date, an offset instant, or a `Z` one, so naming it for a *form* would have been a
  claim it cannot make. `OkfStamp.FormatTimestamp` went with the reconciliation — it
  rendered the same string as `OkfTimestamp.ToCanonical` (verified byte-for-byte across
  offsets, subseconds, and both ends of the range before it was deleted), and one
  canonical form with two renderers is one renderer too many.

### Proposed decisions (pending review): the bundler (work item #5, 2026-08-15)

The first post-MVP item, and the one PRD §1.3 named as a non-goal *for MVP* rather than
forever. `okf bundle` packages a vault's bundles for **consume-only** distribution: it
ships `bundles/` and nothing else, in any of the three shapes SPEC §3 permits, with an
attestation of what shipped. It also settles the cross-bundle-reference spike this
document has carried as an open item since topic 1.

#### The cross-bundle reference SPIKE, resolved: leave the link dangling and say so

When a *subset* of a multi-bundle vault is packaged, a link from an included bundle into
an excluded one has nothing to point at. Three answers were available.

- **(a) Leave it dangling, and record every one in the distribution manifest.
  Adopted.** §6.1 does not merely tolerate this — it says a link whose target is not in
  the bundle "is not malformed; it may simply represent not-yet-written knowledge", and
  obliges *every* consumer to cope. A dangling link is therefore a legal artifact of the
  format rather than damage the bundler inflicted, and the honest thing to do with a legal
  artifact is to name it: `okf-bundle.json` lists `externalLinks: [{from, to, bundle}]`,
  and the run prints one warning line per link on stderr. The consumer learns what they
  did not receive, from the distribution itself, without the bundler having invented
  anything.
- **(b) Vendor the linked concepts into the shipped bundle. Rejected.** It violates the
  bundle boundary the whole model rests on, and it duplicates *trust state*: a vendored
  copy carries its own `generated`, its own `verified`, its own `stale_after`, and its own
  provenance, all of which now describe a document that lives somewhere else and is
  maintained by someone else. The copy cannot be re-verified (§5's decision test fails on
  it by construction), it will silently diverge from the original, and `OKF0303`'s
  near-duplicate rule would be right to complain about it. A knowledge format whose
  packaging step multiplies documents is a format that decays.
- **(c) Refuse to package unless `--allow-dangling` is passed. Rejected.** It makes the
  tool block what the spec explicitly permits, which is the inverse of the standing
  principle that *defaults block only what the spec says*. It would also fire on the
  common, correct case — a vault whose bundles cross-reference each other, packaged one
  bundle at a time — and a flag that must be passed every time is a flag nobody reads.
  The warning carries the same information at the same moment and costs nothing.

**What the layout buys, and why it is the layout.** A distribution keeps the vault's
`bundles/<name>/` shape rather than hoisting a single bundle to the root. The consequence
is the useful half of the spike's answer: a relative link from `bundles/a/x.md` to
`../b/y.md` **still resolves** when both bundles are packaged, so cross-bundle references
are not broken by distribution at all in the whole-vault case. Only two things dangle — a
link into a bundle the caller left out, and a link into something no distribution ever
carries (`raw/`, `custodian/`, the repository around the vault) — and those two are
exactly what `externalLinks` lists. The manifest records a link as dangling by asking
whether its resolved target is in the shipped file set, not by guessing from its shape.

#### What ships, and what never does

- **Included:** `bundles/<name>/**` — concepts, `index.md`, `log.md`, `about.md` files,
  `references/`, **and non-markdown content**. A bundle is more than its concepts: §6.3's
  `references/` convention explicitly covers code, and Google's own bundles carry `.py`
  attesters and a `viz.html`. `OkfBundle.ContentFiles()` is `MarkdownFiles()` with the
  extension filter removed and every other rule of that walk kept — no dotfiles, no
  dot-directories, no symlink that leaves the bundle root.
- **Excluded, by construction:** everything outside a bundle root. `raw/`, `custodian/`,
  `okf.json`, `.markdownlint.yaml`, and the vault README are not skipped by a rule; they
  are never reached, because the walk starts at `bundles/<name>/`. Q3 called `raw/` a
  producer-side archive and this is the sentence that makes it true: a consumer receives
  the extracted `references/` concept plus the original URL in frontmatter, never the
  captured bytes.
- **Excluded, by name:** editor and tool droppings inside a bundle — `*~`, `*.swp`,
  `*.swo`, `*.swn`, `*.orig`, `*.rej`, `*.bak`, `Thumbs.db`, `desktop.ini`. Deliberately a
  short list matched on the **name**, never on content, and deliberately not extended to
  "anything that is not `.md`": a file whose name does not say it is junk gets packaged,
  because a producer who put it in a bundle root meant it there. `.DS_Store` and
  `.obsidian/` need no entry — the dotfile rule already has them.
- **Not stripped, because it was never there:** the custodian machinery is *beside* the
  bundle rather than inside it (topic 1), so "the bundler strips custodian machinery" is,
  in the end, a walk that starts one directory lower. The design decision was made in
  topic 1; this milestone only had to not undo it.

#### Deterministic archives, and the one clock reading

The same vault, packaged by the same version with the same stamp, is **byte-identical**.
That is a requirement rather than a nicety: a release artifact whose bytes change on every
rebuild cannot be checked against a published digest, and a reproducible archive is what
lets two people confirm they received the same knowledge.

- **Entries are sorted by path, ordinal**, and the manifest sorts with them (`bundles/…` <
  `okf-bundle.json`, so it lands last). **No directory entries** are written: every
  extractor creates the directories a file path implies, and an entry carrying no bytes is
  one more thing that would have to be made deterministic.
- **Every archive timestamp is `1980-01-01T00:00:00Z`**, not the file's mtime and not the
  epoch. Not the mtime because a fresh `git clone` re-stamps every file, so an archive
  built from a checkout would differ from one built from a working tree with no content
  difference at all — the case a release runner hits every time. Not the epoch because zip
  stores an MS-DOS timestamp, which cannot represent anything before 1980; one constant
  both formats can hold beats a smaller number that only one of them can.
- **Ownership is `0:0` with empty owner names, and every file ships mode `0644`.** An
  executable attester loses its bit; that is accepted, because okf-net never executes an
  executor or an attester (PRD §1.3 makes running them a permanent non-goal) and a
  consumer who wants to run one can `chmod`. The alternative — carrying the producer's
  modes — makes the archive depend on a umask.
- **The gzip and zip containers were measured, not assumed.** .NET's `GZipStream` writes a
  header whose MTIME field is zero and which carries no filename, so the compressed stream
  is reproducible; `ZipArchiveEntry.LastWriteTime` is set from a `DateTimeOffset` with an
  explicit zero offset, so the DOS timestamp does not depend on the packaging machine's
  time zone. Both were verified by building the same archive twice, 1.1 seconds apart,
  and comparing bytes — which is also how the test asserts it, with every source file
  re-stamped in between.
- **The tar entry format is GNU, and finding that out is the milestone's sharpest
  lesson.** The first implementation wrote PAX, because PAX is the modern format and
  carries timestamps losslessly. It is not reproducible in .NET: every PAX
  extended-header entry is named `./PaxHeaders.<process-id>/.` — a constant, whatever the
  entry's path — so an archive embeds
  the pid of the process that wrote it and two builds of the same vault differ in a few
  bytes per file. **The determinism test passed anyway**, because both writes happened
  inside one test process; the defect surfaced only when the dogfood vault was packaged
  twice from the shipped binary and the digests were compared, which is the thing the
  claim is actually about. Ustar was the obvious alternative and was rejected on
  measurement too — it throws on a path over 100 characters that cannot be split across
  its name and prefix fields, which an arbitrary consumer's bundle can reach (measured: a
  350-character path throws; GNU writes it through a constant-named long-link entry).
  GNU carries mtime in the header and embeds no pid. It is not entry-free either — a path
  over 100 characters gets a preceding `././@LongLink` block — but that name is a constant
  and carries no host state, which is the property the format choice turns on.
  - **The test that now catches it reads the raw 512-byte header blocks** and asserts that
    every name in the archive is one the plan named. `TarReader` consumes extended headers
    silently, so no test written through the reader can see an entry the writer invented —
    which is exactly how a byte comparison inside one process passes while two release
    builds disagree.
  - **GNU's `atime`/`ctime` are deliberately left unset, and that correction is the
    second half of the lesson.** They were first pinned to `ArchiveTimestamp` "like every
    other timestamp here", which was reasoning by analogy rather than by measurement:
    unset, `System.Formats.Tar` writes those two fields as NUL bytes, so they were already
    constant and pinning them bought no determinism at all. What it cost was
    *interoperability*. GNU puts atime/ctime at byte 345 of the header block, which in
    ustar is where the `prefix` field begins, and CPython's `tarfile` joins `prefix` onto
    the entry name for every non-GNU-typed entry without first checking the archive's
    magic. So `tar -xzf` and libarchive read the archive correctly while Python's standard
    library — the module a large share of consumers will reach for, and the language of
    the reference implementation this repo checks itself against — extracted it into a
    directory named after the octal timestamp: `02263523000/bundles/okf-net/…`. Writing
    NULs is what GNU tar's own writer does for a non-incremental entry, and it costs
    nothing. **The generalizable half:** reproducibility and interoperability are two
    different claims, and byte-comparing two of your own archives establishes only the
    first. The archive is now read back by a second implementation, and the test asserts
    the whole 155-byte ustar `prefix` window is empty — a byte range POSIX defines, not a
    constant read back out of the code.
- **`generatedAt` is the only clock reading anywhere in the packaging path**, and
  `--generated-at <instant>` pins it. This is the reproducible-builds `SOURCE_DATE_EPOCH`
  problem in miniature and it gets the same answer: an honest timestamp by default, an
  injectable one for a release. The tag pipeline passes `$CI_COMMIT_TIMESTAMP`, so the
  published archive is reproducible from the tag alone rather than from the minute the
  runner happened to start.

#### `okf-bundle.json`: the attestation, and where it sits

The distribution manifest sits at the distribution **root** — outside every bundle root —
and carries `manifestVersion`, `okfVersion`, `generator`, `sourceVault`, `generatedAt`,
`bundles`, `externalLinks`, and a `sha256` for every file shipped.

- **Outside the bundle root, because it describes the distribution rather than a bundle.**
  What a consumer receives has to stay exactly the bundle the producer had, so a consumer
  who deletes the manifest — or never notices it — still holds a complete `cat`-readable
  bundle, which is the §1 promise the manifest must not quietly withdraw. It sits where
  `okf.json` sits, for the same reason: a file about the container does not belong inside
  the thing it contains.
  - **The conformance argument first given for this was checked and is wrong**, and the
    correction is worth recording because the wrong version is the intuitive one. §11.1
    asks for parseable frontmatter on "every non-reserved **`.md` file** in the tree" — it
    is scoped to markdown. A `.json` inside a bundle root is therefore *not* a
    frontmatter-less concept and does not fail conformance; measured, a bundle root holding
    both an `okf-bundle.json` and an attester `.py` lints 0/0/0, as it must, or §6.3's
    code-as-content convention could not work at all. The README trap (topic 2) is real and
    is a *markdown* trap. The placement is unchanged; only the reason for it is.
- **The manifest is the one unhashed file.** It cannot record its own digest without a
  fixed point, and a self-attesting manifest proves nothing anyway: the integrity claim is
  only as strong as the channel the manifest itself arrived over. What the hashes are for
  is the mundane and common failure — a truncated download, a half-applied edit, a file
  added to an unpacked copy — not a forgery.
- **`sha256`, and the field is spelled exactly as the capture manifest spells it.** One
  hash convention in the repository: 64 lowercase hex digits, `OkfCaptureManifest.Sha256Of`
  is the single implementation, and `raw/` immutability (`OKF0310`) and distribution
  integrity now answer to the same primitive.
- **Keys are camelCase, not the spec's `okf_version` snake_case.** Frontmatter is YAML read
  by document tooling; this is machine-maintained JSON read by the same reader that reads
  `raw/manifest.json`, and the two JSON files should not disagree about their own
  conventions. The *value* is the spec version (`"0.2"`); only the key's spelling is
  okf-net's.
- **`sourceVault` is a name, never a path.** It records the vault directory's name, or —
  when the vault is the conventional `okf/`, which names nothing — the project directory's
  name, so okf-net's own distribution says `okf-net`. Recording the absolute path would
  ship the producer's filesystem layout to every consumer, which is not information anybody
  asked for.
- **`generator` is a §7 actor** (`okf/1.0.0-rc.15`) carrying the semantic version and
  *not* the `+sha` build metadata: an archive should be reproducible from a tag, and which
  machine compiled the binary is not part of what was packaged.

#### `--verify`, and `--lint`

- **`--verify <archive-or-directory>` re-hashes and reports; exit 1 on any mismatch.**
  Three findings, and the third is the one worth having: a file the manifest lists and the
  distribution lost (**missing**), a file whose bytes changed (**modified**), and a file
  the distribution carries that the manifest never listed (**unlisted**). Without the
  third, adding a file to an unpacked distribution would pass verification, which is the
  attack the check is easiest to write wrongly. An archive is identified by its **magic
  bytes** rather than by its name, because a downloaded artifact may have been renamed and
  the bytes are what has to be read.
  - **An entry that is not a file is *unlisted*, not skipped.** A tar symlink, hard link,
    or device node carries no bytes, so it can never fail a digest comparison — which
    means passing over it let one join a downloaded archive and still verify clean, in the
    shape (tar.gz) that is the default. A planted symlink is the entry that matters, since
    extracting it writes a path into somebody else's filesystem, and it is precisely the
    *unlisted* tamper this rule exists to catch. Directory entries stay skipped: the
    bundler writes none, and every extractor creates the directories a file's path implies.
  - **Nothing is ever extracted.** `--verify` streams each entry and hashes it in memory,
    so a hostile archive carrying `../../x`, an absolute path, or a symlink entry is
    *reported* and never written anywhere — there is no extraction directory to escape
    from. `--lint` does materialize a directory, but it materializes the *plan* through
    the same writer that produces the `dir` format, never an archive's own paths, so no
    path from an untrusted archive reaches the filesystem either.
  - **A zero-length file must survive the round trip.** tar stores an empty file as a
    header with no data section, which `TarReader` surfaces as a null `DataStream`;
    reading that as "not a file" made `--verify` call the bundler's own output *missing*
    a file it had just written. Zip and the directory shape were correct all along, which
    is why a per-format verification test is worth the three lines it costs.
  - **A truncated archive is a finding, not an exit 2.** A cut-off download is the exact
    failure the digests exist for, and `EndOfStreamException` — unlike the
    `InvalidDataException` a not-an-archive raises — *is* an `IOException`, so it did not
    crash: it fell through to the command's environment-failure handler and came out as
    exit 2 with a bare "Unable to read beyond the end of the stream" naming no file. Both
    now render as *unreadable* and exit 1.
- **A distribution with no manifest is *unreadable*, not valid.** "Nothing to check" must
  never render as a pass; `okf bundle --verify` on a directory the bundler never wrote
  exits 1 and says so.
- **`--lint` lints what shipped, as a stranger sees it: default severities, no
  `okf.json`, no vault.** Linting the *source* bundles would have been one line shorter
  and would have answered a different question — the producer's config promotes rules, so
  a vault that lints clean under its own contract says nothing about how the distribution
  reads to someone who has neither the config nor the vault. It is asserted as a real
  difference rather than argued: the test packages a vault whose config promotes `OKF0301`
  to error and shows the same concept reported at *warning* in the distribution and at
  *error* in the vault. `VaultRoot` is deliberately left null — a distribution has no
  vault machinery by definition, which is exactly what makes `OKF0310` inapplicable.
  - **What it lints is a materialization of the same plan**, written by the same writer
    that produces the `dir` format, into a temporary directory that is removed afterwards.
    No extractor ships: reading an archive is needed for `--verify` and writing one is
    needed for packaging, but *un*packing is `tar -xzf`'s job and a consumer already has it.
  - **`OKF0309` (link-leaves-bundle) at info is the expected shape, not a defect**, for a
    subset carrying cross-bundle links: the rule reports precisely the links
    `externalLinks` records. `--lint` fails the run only on errors, i.e. only on §11
    conformance, which is the same contract `okf lint` has everywhere else.

#### Smaller calls, recorded because they will be asked about

- **tar.gz is the default** (the Apache/OSS distribution norm, and the one shape that
  preserves ordering and modes on every platform without a second tool); `zip` and `dir`
  are the other two §3 shapes. The format follows the `--out` name when it says something
  (`.zip`), and `--format` always wins. A `dir` distribution *is* the git-repository shape
  §3 recommends — commit it and the recommendation is satisfied — so no fourth format was
  added for it.
- **An archive is overwritten; a directory is not.** An archive file is wholly generated
  output, like a regenerated `index.md`, and re-running must be safe for a release job. A
  *directory* may be anything, so the bundler writes into one only when it is empty, new,
  or holds a readable `okf-bundle.json` — in which case exactly the files that manifest
  lists are deleted, the emptied directories pruned, and the new distribution written. It
  never recursively deletes a directory it cannot prove it wrote, and a re-run therefore
  leaves nothing stale behind for `--verify` to report as *unlisted*.
- **An unknown `--bundle` name is refused before anything is written**, listing the names
  that are available. Silently packaging nothing, or packaging everything, are both worse
  answers to a typo.
- **Deferred, deliberately.** (a) *Signing.* Hashes without a signature are integrity, not
  authenticity; a signature needs a key, a distribution channel for the public half, and a
  policy for rotation, none of which exists yet. The manifest's shape leaves room for a
  detached signature over it. (b) *Per-bundle archives* — one archive per bundle in a
  multi-bundle vault. `--bundle` plus a loop is the same thing, and until someone wants it
  the flag is not worth inventing. (c) *An extractor* (`okf bundle --extract`), for the
  reason above. (d) *Incremental or delta distributions*: the whole artifact is a few
  hundred kilobytes.

#### The scoped mutation run, and what it found

`dotnet stryker --mutate` over the four new files: **54.64% → 67.33%** after the survivors
were read (374 mutants tested, 270 killed). The score is above the repo's `break`
threshold of 60 and within a few points of the solution baseline, and the exercise earned
its keep twice, in defects rather than in tests:

- **A link naming a *directory* was always reported as dangling.** `../other-bundle/`
  normalizes with its trailing separator intact, so the comparison against the packaged
  paths never matched, and a link into a bundle that *was* shipped came out in
  `externalLinks`. The separator is now trimmed before the target is compared. This is the
  exact case the spike's answer turns on, and no test had it.
- **`okf bundle --verify` crashed on a file that is not an archive.**
  `InvalidDataException` is not an `IOException`, so a 404 page saved as `okf.tar.gz` —
  the most likely thing anybody will ever point `--verify` at by mistake — escaped the
  command's handlers and killed the process instead of exiting 1 with a sentence.

Ten tests came out of the rest, all of them contracts rather than change-detectors: entry
and manifest ordering are one order and it is ordinal; the packaged bundle list is sorted
by name whatever order `--bundle` was given in; dangling links dedupe and sort by
document, then line, then target; archive entries carry no owner names and a normalized
mode; a bundle with no vault around it records `sourceVault: null`; a stamp with no offset
reads as UTC (a local reading would make the same command on two laptops write two
manifests); the inline `--out=` form; and `--lint` off by default.

**Not chased, and the classification is the same one the mutation-testing milestone
recorded:** 39 string-literal mutants on diagnostic and report prose, 25 mutants that
delete an argument guard (`ArgumentNullException.ThrowIfNull`), and a tail of equivalents —
`leaveOpen: true → false` on a stream that is disposed by its owner anyway, `Append →
Prepend` before a sort, `separator <= 0` where the value cannot be 0, and the `IsZip`
magic-byte conjunction, which no input distinguishes because a file that starts `P` but
not `PK` is not a zip either way.

### Proposed decisions (pending review): the static site milestone (work item #6, 2026-08-15)

`okf site [path] --out <dir>` renders a resolved vault as a static website. Ringo's
design input on the work item is the specification: a trust dashboard of rounded stat
tiles that are **clickable filters**, an Obsidian-style force-directed graph, browsable
concepts. Editing and verifying are deliberately out of scope; they have their own item.
Google's `reference_agent/viewer` is the prior art — its embedded-JSON bundle, its rewired
internal links and its backlink index all survive here; its CDN-loaded Cytoscape and
`marked` do not, for reasons below.

**Multi-page by default, single file on request.** One HTML file per markdown file,
mirroring the bundle tree, plus `index.html` (the dashboard) and `graph.html`. That is
what makes a URL name a concept: a deep link is shareable, the back button works, and a
browser tab title says which concept you are reading. `--single-file` emits the same site
as one `index.html` carrying every article as embedded JSON, routed on the fragment, for
the other thing Ringo asked for — handing someone a knowledge base as one attachment.
The second mode was cheap **because** the first was built as a model plus fragment
builders rather than as a template: `OkfSiteBuilder` produces an `OkfSiteModel`, and two
emitters consume it, sharing every fragment. Only link resolution differs (a relative
`.html` path versus `#c=<id>`), which is why the model is built per mode instead of once.
Cost of the single-file mode, stated: a concept's own heading anchors do not survive,
because the fragment is spent on routing.

**Every link is relative; nothing is loaded at run time.** The site must work identically
from GitLab Pages and from a `file://` path, which rules out absolute-root hrefs, and it
must make no network request, which rules out a CDN. Both are also privacy positions: a
knowledge site that fetched a script on every page view would report the reader's browsing
to a third party, and a vault is exactly the kind of reading nobody wants reported.

**No third-party JavaScript at all — not vendored, not CDN-loaded.** The work item offered
Cytoscape (Google's choice), D3 or vis, all permissively licensed. All three were declined
in favour of ~250 lines of our own: a Fruchterman-Reingold layout on a `<canvas>`, plus the
dashboard's filtering. The reasoning is not "not invented here":

- **There is no JavaScript toolchain in this repo to vendor with.** mise installs `dotnet`
  and `markdownlint-cli2` and nothing else. Vendoring means committing a pre-built
  `cytoscape.min.js` that nobody here built and nobody here can rebuild — a supply-chain
  artifact taken on faith, in a repository whose whole dependency posture is an SBOM and a
  license gate.
- **The weight is real.** `cytoscape.min.js` is ~400 KB. It would ship inside the `okf`
  binary, be copied into every generated site, and be inlined into every single-file
  export. Ringo reads this on a phone.
- **The feature list is small.** Trust-tier colouring, a type-colouring toggle, click to
  open, drag, pan, zoom, hover label, stale ring. That is a fraction of what a graph
  library is for, and the layout is a textbook algorithm.

Markdown is a different question and got the opposite answer: **Markdig 1.3.2 (BSD-2-Clause,
allowlisted) renders bodies server-side**, so no `marked.js` is needed either. A CommonMark
implementation is not 250 lines, and rendering at generation time rather than in the reader's
browser means the pages are readable with JavaScript off. Verified: `mise run licenses` reports
`Markdig 1.3.2 BSD-2-Clause`, and `mise run publish-aot` is zero-warning with Markdig on the
NativeAOT path — the published binary generates the dogfood site.

**The stylesheet and the client script ship as embedded resources.** They stay editable,
diffable, lintable files under `src/Okf.Core/Assets/` and are read back with
`GetManifestResourceStream`, which is AOT-safe: a manifest resource is data in the image, not
a type the trimmer has to be told to keep. The alternative — C# string literals — would have
made a 500-line stylesheet unreviewable.

**Raw HTML in a concept body is escaped, not emitted** (`MarkdownPipelineBuilder.DisableHtml`).
A bundle is data okf-net did not write (PRD ACC-1), and a generated page is opened from
`file://`, where a `<script>` smuggled through a body would run with no origin to contain it.
It also buys the well-formedness guarantee below, which passthrough HTML cannot: nothing
upstream validates it. The cost is visible and accepted — a foreign bundle using `<br>` shows
the tag as text. okf-net's own generated-index marker is stripped before rendering, because it
is the one HTML comment whose provenance the generator knows.

**A link destination carries an allowlisted scheme or none at all** — `http:`, `https:`,
`mailto:`, or a relative path. Disabling raw HTML shuts one door into the page and not the
other: `[x](javascript:alert(1))`, `<javascript:alert(1)>` and a reference definition pointing
at one are all ordinary markdown, none of them is an HTML tag, and each renders an anchor a
reader can click — on a `file://` page, with no origin to contain it. So the scheme is read on
the AST before the destination is resolved, and for an image's `src` as well as an anchor's
`href`. It is read the way a browser reads it rather than the way `Uri` does:
case-insensitively, and with ASCII whitespace and C0 controls dropped first, because a leading
space or a tab spliced in by a numeric character reference does not stop a browser navigating
to a `javascript:` URL, and Markdig has already decoded that reference. A destination outside
the allowlist is percent-escaped into one inert relative segment and marked `broken` — §6.1
says mark, never drop, and the reader still sees what was written. The cost is stated: `tel:`
links and `data:` images are refused as well. Adding a scheme is a one-line change to the
allowlist; the default is the small set a knowledge site needs.

**Every page is well-formed XML, and the suite parses it.** Attributes are quoted, boolean
attributes carry values, void elements are self-closed, and inline `<style>`/`<script>` bodies
are wrapped in a comment-hidden CDATA section (`/*<![CDATA[*/ … /*]]>*/`) — a wrapper browsers
read as a JavaScript comment. The embedded JSON needs no wrapper: it is written with
`Utf8JsonWriter`'s **default** encoder rather than `UnsafeRelaxedJsonEscaping`, so `<`, `>` and
`&` arrive as `\u003C` and its siblings — the payload can neither close its own `<script>`
element nor break the parse. `OkfSiteGeneratorTests` runs every page of both modes through `XmlReader`;
an unclosed element or an unescaped ampersand fails a test rather than a reader's browser.

**Only concepts are counted, graphed and back-linked.** Reserved files (§3.1) become pages —
that is what makes the index hierarchy browsable and what breadcrumbs are built from — but a
generated `index.md` links every concept in its directory, so letting index links into the
graph would connect everything to everything, and letting them into "Cited by" would tell a
reader that a concept is cited by its own table of contents. The tiles count concepts for the
same reason: counting the tables of contents inflates every number.

**Colour is a status channel, and never the only one.** The five trust and lifecycle tiles
carry the reserved status palette — human-reviewed `good`, machine-confirmed `warning`,
unverified neutral, stale `critical`, draft `serious` — and the two structural tiles (Bundles,
Concepts) carry the accent, so "how much is here" never wears a status hue. Every tile also
carries a glyph and a word, and every number stays in ink rather than in the status colour;
that is what keeps the dashboard legible to a reader who cannot separate the hues, and what
lets the sub-3:1 status steps be used at all. Both themes are selected rather than flipped:
the dark block re-steps surfaces and inks against the dark plane, and the status hexes, which
clear 3:1 there, stay put. The graph's optional colour-by-type mode draws from the validated
categorical order and always ships a legend — past three simultaneous types those hues are
below the all-pairs separation floor, so the legend and the node labels are doing the work.

**Filter state lives in the URL fragment.** `#f=stale&b=okf-net&q=…` on the dashboard,
`#c=<id>` in single-file mode. A filtered view is therefore linkable and survives a reload,
which matters most in the case with no server to hold state: a `file://` page.

**Generation is deterministic.** The graph's layout uses a seeded PRNG and a fixed iteration
count, nothing reads a clock beyond today's date (injected, per CORE-7), and page order is
ordinal. Two runs over an unchanged vault produce byte-identical output, which a CLI test
asserts — a site that churned on every run would be unusable to diff or to publish.

**`--out` may not point inside a bundle being rendered.** The next run would otherwise read
its own HTML, and `okf lint` would find a bundle full of pages. Refused as a usage failure
(exit 2) before anything is written. Nothing is ever deleted from the output directory
either: an output directory belongs to the caller, and a generator that removed files it did
not write is one `--out ~/` away from a disaster.

**Deliberately not done.** No CI `pages` job — a commented block sits at the end of
`.gitlab-ci.yml` for whoever enables it, but publishing an unreviewed design to a public URL
is not a side effect anyone asked for, and Ringo wants to iterate locally first
(`mise run site`). No copying of non-markdown assets into the site, so an image beside a
concept does not travel with it; the link is left exactly as written, per §6.1's tolerance
rule. No search index — the dashboard's filter box is a substring match over titles, types,
tags and descriptions, not BM25; `okf search` is the ranked search, and duplicating its
scoring in JavaScript would be a second implementation of a judgement the library already
makes. No dogfood concept was added for the tag registry's sake: a concept about the site
wants a `site` tag, `okf/okf.json` holds the closed registry, and that file is off-limits on
this branch (three work items are in flight at once). It is a one-line follow-up.
