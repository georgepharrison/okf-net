# okf-net

.NET toolset for OKF v0.2 (Open Knowledge Format). See `docs/decisions.md` for
all architecture decisions — agents must read it before making design or
implementation choices in this repo.

## Rules

- **Commits.** All commits except merge commits MUST follow [Conventional
  Commits v1.0.0](https://www.conventionalcommits.org/en/v1.0.0/). Enforced by
  a `commit-msg` git hook.
- **Setup.** Run `mise run setup` once after cloning to install git hooks.
- **Markdown lint.** Linted with `markdownlint-cli2` (config in
  `.markdownlint.yaml`). Dot-folders (e.g. `.claude/`, `.github/`) are
  excluded.
- **Tests must be shown to constrain the code.** A test added alongside new
  behavior must be demonstrated to FAIL without that behavior (write it
  first, or temporarily revert the change and run it), OR derive its expected
  values from an independent oracle (the Python reference implementation, the
  spec text) rather than from the code under test. State which in the commit
  or report. Assertions must not be satisfiable vacuously (e.g.
  `Contains("0 errors")` also matches `"10 errors"`). Reviewers: spot-check by
  mutating the code under test and confirming tests go red.
