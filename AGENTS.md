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
