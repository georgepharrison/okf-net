# Captain brief — polish step H (#14 composed method), one area

Lane brief used for #14 lanes H1–H2 on 2026-08-16; H3/H4 should follow it
verbatim (any orchestrating model). Paths under `/tmp` in it refer to the
orchestrator's scratchpad — the inventory now lives at
`docs/spikes/2026-08-16-composed-method.md`.

You own one area of okf-net's #14 pass end to end: refactor → spawn ONE adversarial reviewer
(opus) → apply fixes → rebase → gate → push → MR to `dev`. Do NOT merge; do NOT poll the
pipeline. Final report ≤ 30 lines. Sequential lane, budget-conscious: be as verbose as the work
needs and no more.

## Rules

- AGENTS.md is binding: names carry meaning (a well-named private method IS the documentation);
  comments only for a constraint / spec section / non-obvious why; Conventional Commits with the
  trailer lines your Bash instructions specify; never `--no-verify`; **NO `!` types, NO
  `BREAKING CHANGE:` footers** (1.x forever); style rules are build errors (`_camelCase` private
  fields, explicit types in src/, no `this.` on fields) — run `dotnet format` before committing.
- `glab issue view 14`: Uncle Bob "extract till you drop" / Kent Beck Composed Method — public
  methods read as a chain of well-named private methods, ~20 lines per method as the target;
  readability over method-count frugality; file-by-file, tests green throughout.
- **Public API of Okf.Core is frozen in shape** (namespaces were re-homed in #59; signatures
  stay). Extract PRIVATE (or `internal` only when tests genuinely need it — prefer not) methods.
  No behaviour change: `okf` outputs byte-identical; the mutation score of every touched file
  must not drop (run `dotnet stryker` scoped to your files once at the end; before = the
  per-file numbers you measure at the start of your lane with the same scoped command — do
  that first, it is your baseline).
- Extraction discipline: one concept per method; parameters ≤ 4 (else a small private record);
  no boolean flag parameters that switch behaviour (split into two methods); keep the public
  method as the readable narrative; do not create "helper" grab-bags; do not move code across
  files or types (that is #15's job) except when a private method clearly belongs to a type
  it already exists on.
- Docs: no AD changes expected. Append `### Proposed decisions: composed method — <area>
  (work item #14, 2026-08-16)` to docs/decisions.md ONLY if you made a judgement call worth
  recording (e.g. a method deliberately left long because splitting hid the algorithm — say
  which); otherwise a one-paragraph note in docs/spikes/2026-08-16-composed-method.md
  (create it in the first lane with the inventory; later lanes append their area's before/after).

## Inventory (first lane creates it; later lanes update their area rows)

`docs/spikes/2026-08-16-composed-method.md`: per file under src/: number of methods > 20 lines,
longest method (name, lines), and the area's before/after after your lane. Measure with a small
script (Roslyn via `dotnet script`? no — keep it simple: a python line-counter over `{ }`
depth is fine as long as it is stated as approximate) in the scratchpad
/tmp/claude-1000/-home-ringo-code-okf-net/d5c1d513-137f-4339-af06-cc2386934c51/scratchpad/composed/.

## Reviewer instructions (paste to the opus reviewer)

Verify: behaviour preserved (build origin/dev in a scratch dir; diff `okf help`, `okf lint okf/`,
`okf search --json <q>`, `okf index --json okf/bundles/okf-net`, `okf inbox --json`, and for
Cli lanes every verb's `--help`); every extracted method's name says what it does (flag
narrative comments and names like `Helper`, `Process`, `DoWork`, `Handle2`); no public
signature changed (`git diff origin/dev..HEAD -- src/Okf.Core | grep -E '^[-+].*public'`); no
new `internal` unless justified; mutation score per touched file ≥ before (rerun scoped
stryker if the captain's numbers look off); full gate: `dotnet build` 0 warnings, `dotnet
test`, `mise run lint`, `mise run cli -- lint okf/`, `mise run cli -- index --check
okf/bundles/okf-net`, `mise run trim-check`, `mise run test-install`. Fix real defects, no
restyling, one round, report verified/fixed/accepted.

## Finish

Rebase onto origin/dev, gate, push, `glab mr create -s <branch> -b dev -t "refactor(<area>):
compose <what> into readable steps (#14)" -d "Polish step H, <area>" -y`. Report: files touched,
methods > 20 lines before → after per file, mutation before → after per file, reviewer findings,
anything deliberately left long and why.
