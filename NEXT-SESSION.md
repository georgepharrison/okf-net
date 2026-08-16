# NEXT-SESSION.md — handoff prompt for the okf-net orchestrator

> Paste the **Prompt** section below as the first message of a new Claude Code
> session started in this repo. Everything above it is context for humans.

## Why this file exists

The previous session (2026-08-14 → 2026-08-15) built okf-net from `git init`
to the edge of 1.0.0. That thread grew so long that every turn re-read a full
day of build logs; a fresh session with this file is far cheaper. All state
lives in git, GitLab, and these docs — nothing depends on the old thread.

## Where things stand (2026-08-16, end of session)

- **`v1.0.0` shipped 2026-08-16** (`git tag`; `glab release view v1.0.0`).
  `main` is the **stable release branch**, `dev` the `rc` prerelease channel
  (`.releaserc.yml`); both are protected. Every MR targets `dev`; `dev` is
  promoted to `main` only when Ringo says ship. `dev` currently cuts
  `v1.1.0-rc.N` (`glab release list`).
- Merged into `dev` since 1.0.0, all closed: #53 bare release versions, #44
  write bookkeeping (`okf capture add|close`, `okf generated stamp`), #43
  registry + `--scope`, #23 `okf upgrade`, #56 AGENTS.md context pointer, #51
  shell completions. `git log --oneline main..origin/dev` confirms.
- Artifact host is fixed: iac MR !34 added the crond caps busybox needs and
  the image was rebuilt on tycho — `get.okf.tychostation.dev` serves all 8
  assets of v1.0.0 (7 release files plus its own `latest.json`), and the
  15-minute sync is current.
- Next in order (open issues, `glab issue list`): #45 multi-model acceptance
  (Qwen first) → #46 optional-family validation + `status` in search → #49
  first-run papercuts + thesis-first README → then the `phase:post-1.0`
  lanes. Design calls waiting on Ringo: #54 (register locking), #55
  (source-pin policy), #57 (`okf concept move`), #58 (`okf bundle add`), #50
  (spike).
- Operating model unchanged (delegate, Sonnet/Opus split, all reviews), plus
  three things learned this session: `dev` merges do NOT auto-close issues
  (GitLab's default branch is `main`; only merges there fire `Closes #N`) —
  close by hand with a note; a hand-typed `generated.at` is forbidden, use
  `mise run cli -- generated stamp <concept> --by <actor>`; and reserve type
  names/verbs/AD numbers up front in parallel lanes — a
  `docs/decisions.md`/`docs/architecture.md` tail conflict now resolves
  keep-both-`dev`'s-first, AD ids ascending.

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

Continue the okf-net project as ORCHESTRATOR. Read NEXT-SESSION.md, then
AGENTS.md, docs/architecture.md, the last two sections of docs/decisions.md,
docs/lessons.md, and the GitLab board. Do not do leaf work yourself: delegate
every build to a subagent (Sonnet for mechanical/well-specified work, Opus for
design-heavy builds and for EVERY review), run an adversarial review agent
before every push, merge only on a green pipeline, keep the board labels
current, and record decisions in docs/decisions.md as proposals when a call is
small. First, ask Ringo whether dev (now at v1.1.0-rc.N) should be promoted to
main as 1.1.0 — a question to raise at session start, not a default. Then work
order: #45 → #46 → #49, with every MR targeting dev; raise #54, #55, #57, #58
and #50 with Ringo as design calls when they come up. Close issues by hand on
merge (dev merges do not auto-close them) and stamp generated concepts with
`mise run cli -- generated stamp`, never by hand. Stop and ask when a
decision, a credential, a GitLab setting, or a promotion of dev to main needs
Ringo. Be economical: keep status messages short, batch questions, and never
re-derive what the docs already record.
