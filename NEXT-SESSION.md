# NEXT-SESSION.md — handoff prompt for the okf-net orchestrator

> Paste the **Prompt** section below as the first message of a new Claude Code
> session started in this repo. Everything above it is context for humans.

## Why this file exists

The previous session (2026-08-14 → 2026-08-15) built okf-net from `git init`
to the edge of 1.0.0. That thread grew so long that every turn re-read a full
day of build logs; a fresh session with this file is far cheaper. All state
lives in git, GitLab, and these docs — nothing depends on the old thread.

## Where things stand (2026-08-16 polish day, end of session)

- **`v1.0.0` shipped 2026-08-16.** `main` is the **stable** release branch,
  `dev` the `rc` prerelease channel; every MR targets `dev`. `dev` is
  promoted to `main` only on Ringo's explicit word — it has **not** been
  promoted yet, and `dev` is now ~40 MRs ahead, cutting `v1.1.0-rc.N`. **Ask
  Ringo at session start whether to promote.**
- Merged on `dev` since 1.0.0 (features): #53 bare release versions, #44
  write bookkeeping, #43 registry + `--scope`, #23 `okf upgrade`, #56
  AGENTS.md context pointer, #51 shell completions.
- Also merged: the polish plan, steps A–G:
  - **A** — analyzers on `latest-all` + `.editorconfig`; #31 both halves,
    including `_camelCase`/no `var` enforced as build errors.
  - **B** — a trim-check CI job, a per-MR AOT-trimming proxy.
  - **C** — mutation baseline: 73.23%
    (`docs/spikes/2026-08-16-mutation-baseline.md`).
  - **D+E** — dead code + test-gap sweep across six packages; #13 closed;
    package scores now 82–91%.
  - **F** — contextual DRY: 8 unifications; three rows left "open for
    Ringo" in `docs/decisions.md`.
  - **G** — #59 vertical slices: namespaces follow folders, `IDE0130` on.
  - A commit guard now refuses `!`/`BREAKING CHANGE` (the project stays 1.x
    forever); fixes #52 and #61 also landed.
- **IN PROGRESS: #14 composed method.** H1 (Core Documents+Lint) and H2
  (Core Index/Search/Trust) are merged. **H3 (Core Vault/Capture/Bundle/
  Site/Skills/Upgrade) has not started beyond a baseline measurement —
  resume it next.** Then H4 (`Okf.Cli`), then #15 (SOLID/DI/boundaries —
  audit-first proposal in `docs/decisions.md`, no DI container, AOT stays a
  constraint), then close-out: a fresh `mise run mutate` (~22 min), a docs
  truth pass, Ringo runs `/ultrareview` from a **local** session on `dev`,
  fix findings, then promote. The captain-brief pattern for H lanes: one
  Opus captain per area spawns one Opus reviewer, orchestrator merges on
  green, lanes run sequentially, and per-file Stryker scores are measured
  before/after. The inventory and lessons live in
  `docs/spikes/2026-08-16-composed-method.md`.
- Open decisions waiting on Ringo: #54 (register locking), #55 (source-pin
  policy), #57 (`okf concept move`), #58 (`okf bundle add`), #60 (stamp
  fallback drops YAML comments), #62 (bare-CR splitting), #50 (spike), plus
  the three DRY rows noted above. Next feature work after polish, in order:
  #45 → #46 → #49.
- Operating notes: issues don't auto-close on `dev` merges — close by hand
  with a note; never hand-type `generated.at`, use `mise run cli --
  generated stamp <concept> --by <actor>`; SSH needs Ringo's hardware-key
  touch (`ssh -T git@gitlab.tychostation.dev -p 2222`), HTTPS fallback via
  an explicit URL plus the `glab` credential helper; tycho artifact-host
  redeploys need Dockhand's "build" toggled on for the sync image; Ringo
  prefers sequential lanes over parallel ones when the usage budget is
  tight.

## Read these first (in this order)

1. `AGENTS.md` — binding rules (conventional commits, tests must constrain,
   Apache-2.0-compatible deps, code style, `mise run cli --` as the okf
   equivalent in-repo).
2. `docs/architecture.md` — the Architecture Spine: 50 ADs, the invariants.
3. `docs/decisions.md` — the "why"; read the **1.0.0 review** section and the
   two most recent sections; skim the rest by heading.
4. `docs/lessons.md` — operational and process lessons (Docker pool collision,
   pwsh/xdg-open leak, review-is-where-correctness-comes-from).
5. The GitLab board: `glab issue list` (+ `--label phase:post-1.0`).
6. `docs/prd.md` only when a requirement id is cited.

## The outside review (GPT-5.6 via Pi, 2026-08-15)

Verdict: "already a compelling agent knowledge substrate and unusually mature
engineering for an RC; not yet proven as a sustainable knowledge-maintenance
product." Its framing to keep: *LLM Wiki is the programming model, OKF is the
ABI, okf-net is the compiler/toolchain*; and the thesis line for the README:
**"a trustworthy, portable implementation of the LLM Wiki pattern, built on
OKF — agents accumulate knowledge without humans surrendering provenance,
review, portability, or control."** Its P0s became #41–#46. Getting a second
model to review builds is now a standing practice (see #45).

## Operating model that worked

- **Orchestrator delegates; it does not do leaf work.** One `Agent` per work
  item: branch → build → adversarial review (a *second* agent) → push →
  pipeline → MR "Closes #N" → merge → close → next.
- **Model choice:** Sonnet for well-specified/mechanical work (docs passes,
  config, small fixes, renames, CI YAML); Opus for builds with design freedom,
  ALL reviews, anything touching Okf.Core semantics or security. Fable
  (the orchestrator) writes prompts, merges, resolves conflicts, talks to Ringo.
- Parallel lanes only when files are disjoint; use `isolation: worktree` or a
  manual worktree per lane; serialize merges through the orchestrator; expect
  `docs/decisions.md`/`docs/architecture.md` tail-append conflicts (keep
  both, `dev`'s first; AD ids ascend).
- Every builder: conventional commits, the two trailers, never `--no-verify`,
  never merge, report back. Every reviewer: fix real defects, don't restyle,
  mutation-spot-check claims, verify claims against the code not the report.
- Board hygiene: `status:in-progress` on start, unlabel on close;
  `status:blocked` + a note when a human is needed; new items via
  `glab issue create` with the source of the ask.
- Ringo's stop conditions: anything needing credentials/GitLab settings he
  must click; CI failing twice on the same cause; genuine design decisions
  (he answers `k`/`c`/`d`/`talk`); and now that `main` is the stable branch,
  every promotion of `dev` to `main` needs an explicit go, because that merge
  is what cuts a version.
- Push path: SSH normally; if the LAN path to tycho is dark, git works over
  Tailscale via `-c http.curloptResolve=gitlab.tychostation.dev:443:<solgate-ip>`
  and the HTTPS remote with the glab credential helper (already configured).

## Prompt

Continue the okf-net project as ORCHESTRATOR — this handoff is
**model-agnostic**: any orchestrating model (Fable, GPT/Grok via Pi, etc.)
can run it with `glab`, `mise`, dotnet 10, and the same review-before-push
discipline below. Read NEXT-SESSION.md, then AGENTS.md, docs/architecture.md,
the last two sections of docs/decisions.md, docs/lessons.md,
docs/spikes/2026-08-16-composed-method.md, and the GitLab board. Do not do
leaf work yourself: delegate every build to a subagent (Sonnet for
mechanical/well-specified work, Opus for design-heavy builds and for EVERY
review), run an adversarial review agent before every push, merge only on a
green pipeline, keep board labels current, and record decisions in
docs/decisions.md as proposals when a call is small. `mise run lint`
(markdownlint over docs/) must stay clean throughout.

First, ask Ringo whether `dev` (now ~40 MRs ahead, cutting `v1.1.0-rc.N`)
should be promoted to `main` — raise this at session start, not as a default.

Then resume #14 composed method: H3 (Core Vault/Capture/Bundle/Site/Skills/
Upgrade) has not started beyond a baseline — follow
`docs/process/composed-method-lane-brief.md` verbatim for H3 and H4 (one Opus
captain per area, one Opus reviewer, sequential lanes, before/after Stryker
per file), merging on green. Then #15 (SOLID/DI/boundaries — audit-first
proposal already in `docs/decisions.md`; no DI container, AOT stays a
constraint). Then close out the polish phase: a fresh `mise run mutate`
(~22 min), a docs truth pass, and ask Ringo to run `/ultrareview` from a
local session on `dev`; fix what it finds, then ask again about promoting.

Raise #54, #55, #57, #58, #60, #62, #50, and the three open DRY rows in
`docs/decisions.md` with Ringo as design calls when they come up. Next
feature work after polish, in order: #45 → #46 → #49.

Close issues by hand on merge (`dev` merges do not auto-close them) and stamp
generated concepts with `mise run cli -- generated stamp`, never by hand.
Prefer sequential lanes over parallel when the usage budget is tight. Stop
and ask when a decision, a credential, a GitLab setting, or a promotion of
`dev` to `main` needs Ringo. Be economical: keep status messages short, batch
questions, and never re-derive what the docs already record.
