# NEXT-SESSION.md — handoff prompt for the okf-net orchestrator

> Paste the **Prompt** section below as the first message of a new orchestration
> session started in this repo. Everything above it is context for humans.

## Why this file exists

The previous session (2026-08-14 → 2026-08-15) built okf-net from `git init`
to the edge of 1.0.0. That thread grew so long that every turn re-read a full
day of build logs; a fresh session with this file is far cheaper. All state
lives in git, GitLab, and these docs — nothing depends on the old thread.

## Where things stand (2026-08-17 polish close-out)

- **`main` is stable `v1.0.0`.** Never promote `dev` without Ringo's explicit
  instruction. This close-out session was explicitly told not to promote.
  Merge requests still target `dev`, the `rc` prerelease channel; nothing
  else writes to `main`.
- `dev` contains the feature work after 1.0.0 and polish steps A–G: analyzers,
  trim checking, the mutation baseline and test-gap sweep, contextual DRY,
  vertical slices, and the 1.x commit guard. The three unresolved contextual
  DRY rows remain in `docs/decisions.md`.
- **Polish #14 and #15 are complete and closed.** All four composed-method
  lanes are merged: H1/H2 first, H3 in !51, and H4 in !52. The audit-first
  SOLID/DI/boundaries pass is merged in !53; it kept the no-DI-container and
  NativeAOT constraints.
- Close-out has two sibling lanes: the full mutation rerun on
  `polish-mutate-2026-08-17`, and this docs truth pass. After both merge,
  Ringo runs `/ultrareview` from a **local** session on `dev`; findings are
  fixed before feature work resumes in this order: #45 → #46 → #49.
- Open decisions waiting on Ringo: #54 (register locking), #55 (source-pin
  policy), #57 (`okf concept move`), #58 (`okf bundle add`), #60 (stamp
  fallback drops YAML comments), #62 (bare-CR splitting), #50 (guided setup
  spike), and the three open DRY rows: JSON newline pinning, the extra help
  line on IO failure, and an injected reader for inbox.
- The operating model is unchanged: adversarial review before push, merge
  requests to `dev`, no writes or promotions to `main` without Ringo. Issues
  do not auto-close on `dev`; close them by hand after merge and clear stale
  board labels. Never hand-type `generated.at`; use `mise run cli -- generated
  stamp <concept> --by <actor>`.

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

- **Orchestrator delegates; it does not do leaf work.** One agent per work
  item: branch → build → adversarial review by a second agent → push →
  pipeline → MR to `dev` → merge → close the issue by hand → next.
- **Model choice follows the work, not this handoff.** Use an economical agent
  for well-specified mechanical changes and the strongest available agent for
  design-heavy builds, every review, and anything touching core semantics or
  security. The orchestrator writes prompts, merges, resolves conflicts, and
  talks to Ringo.
- Parallel lanes only when files are disjoint; use `isolation: worktree` or a
  manual worktree per lane; serialize merges through the orchestrator; expect
  `docs/decisions.md`/`docs/architecture.md` tail-append conflicts (keep
  both, `dev`'s first; AD ids ascend).
- Every builder: conventional commits, the task's required trailers, never
  `--no-verify`, never merge, report back. Every reviewer: fix real defects,
  don't restyle, mutation-spot-check claims, and verify claims against the code
  rather than the report.
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

Continue the okf-net project as ORCHESTRATOR. This handoff is model-agnostic:
use the available coding agents with `glab`, `mise`, dotnet 10, and the same
review-before-push discipline. Read NEXT-SESSION.md, then AGENTS.md,
docs/architecture.md, the last two sections of docs/decisions.md,
docs/lessons.md, and the GitLab board. Do not redo #14 or #15; both are
complete and closed on `dev`.

First reconcile the two polish close-out lanes: the mutation rerun MR from
`polish-mutate-2026-08-17` and the docs truth-pass MR. Merge requests target
`dev`; run an adversarial review before every push, merge only on a green
pipeline, and keep board labels current. After both close-out MRs merge, ask
Ringo to run `/ultrareview` from a **local** session on `dev`, then delegate
and review any findings. `mise run lint` must stay clean throughout.

Do not promote or write to `main`. `main` remains stable `v1.0.0`, and only
Ringo may explicitly authorize a promotion. After the close-out and local
review, feature order is #45 → #46 → #49.

Raise #54, #55, #57, #58, #60, #62, #50, and the three open contextual-DRY
rows in docs/decisions.md only as Ringo design calls. Close issues by hand
after `dev` merges, because they do not auto-close, and clear stale board
labels. Stamp generated concepts with `mise run cli -- generated stamp`,
never by hand. Prefer sequential lanes when the usage budget is tight. Stop
and ask when a decision, credential, GitLab setting, or any action involving
`main` needs Ringo.
