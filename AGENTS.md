# okf-net

.NET toolset for OKF v0.2 (Open Knowledge Format). See `docs/decisions.md` for
all architecture decisions — agents must read it before making design or
implementation choices in this repo.

## Rules

- **Commits.** All commits except merge commits MUST follow [Conventional
  Commits v1.0.0](https://www.conventionalcommits.org/en/v1.0.0/). Enforced by
  a `commit-msg` git hook.
- **Setup.** Run `mise run setup` once after cloning to install git hooks.
- **Branches and releases.** Merge requests target **`dev`**, not `main`. `dev`
  is the `rc` prerelease channel: every merge there cuts a `vX.Y.Z-rc.N` tag
  and an installable release candidate. `main` is the **stable** release
  branch — merging to it cuts a plain `vX.Y.Z` — and it is only ever advanced
  by promoting `dev`, as an explicit fast-forward or merge, when Ringo says
  ship. Nothing else writes to `main`. Which version comes out is decided by
  the commit types in the range being released (`feat` minor, `fix`/`docs`/
  `refactor`/`perf`/`ci` patch, a `!`/`BREAKING CHANGE` major), which is the
  other reason a commit type must be honest about what the commit did.
- **Running the CLI.** Nothing puts an `okf` binary on your PATH in this
  checkout, so `mise run cli -- <args>` (e.g. `mise run cli -- lint okf/`) is
  the documented `okf` equivalent here — it is what the skills' escape hatch
  points at, and the form the vault's own docs and `okf/custodian/recipe.json`
  use. Tasks and CI jobs invoke `dotnet run --project src/Okf.Cli --` directly,
  and `mise run publish-aot` leaves a real binary at `artifacts/aot/okf`.
- **Markdown lint.** Linted with `markdownlint-cli2` (config in
  `.markdownlint.yaml`). Dot-folders (e.g. `.claude/`, `.github/`) are
  excluded.
- **Dependencies must be Apache-2.0-compatible.** Only permissively licensed
  NuGet packages (MIT, Apache 2.0, BSD). Check the license of the EXACT
  version being added — packages change licenses between majors
  (FluentAssertions went proprietary at v8; do not use it at any version —
  use **Shouldly** when an assertion library is wanted, else plain xunit
  `Assert`). No copyleft (GPL/LGPL/MPL) and no source-available commercial
  licenses in shipped or test code. CI enforces this in the `licenses` job,
  which builds a CycloneDX SBOM of `Okf.sln` and fails on any component whose
  license is not covered by `scripts/licenses-allowed.json` (run it locally
  with `mise run licenses`).
- **Code style: name things to say what they do.** A name that carries its own
  meaning is the documentation. Comments are for what a name cannot carry — a
  constraint, a spec section, a non-obvious why — never narration of what the
  next line already says. Reviewers flag narrative comments as noise.
- **Tests must be shown to constrain the code.** A test added alongside new
  behavior must be demonstrated to FAIL without that behavior (write it
  first, or temporarily revert the change and run it), OR derive its expected
  values from an independent oracle (the Python reference implementation, the
  spec text) rather than from the code under test. State which in the commit
  or report. Assertions must not be satisfiable vacuously (e.g.
  `Contains("0 errors")` also matches `"10 errors"`). Reviewers: spot-check by
  mutating the code under test and confirming tests go red. `mise run mutate`
  does that spot-check mechanically and exhaustively: Stryker.NET rewrites the
  source thousands of ways and reports every mutation no test noticed.
