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
