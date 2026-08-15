# Lessons learned

Running log of non-obvious things this project learned the hard way, so no
future session (human or agent) relearns them. Newest first within sections.

## GitLab / CI

- `cache:key:files` allows at most **2 files**. Exceeding it doesn't error
  visibly — the pipeline is created with **zero jobs** and marked failed with
  empty `yaml Errors`. Diagnose with `glab ci lint` (validates against the
  live instance) before pushing CI changes.
- Tag pipelines are allowed by our `workflow:` rules but no job matches a
  tag, so a tag pipeline would fail with "no jobs" (work item 10).
- Self-managed GitLab ships security templates (e.g.
  `Security/Secret-Detection.gitlab-ci.yml`) with the instance — no
  gitlab.com component mirroring needed.
- The `glab` CLI's token can push over HTTPS via
  `git config credential.helper '!glab auth git-credential'` — the fallback
  when SSH agent access (hardware key window) expires.

## semantic-release

- On a prerelease branch with no stable release, every releasable commit
  produces the next `1.0.0-rc.N` — bump *type* (feat/fix/docs) does not
  change the target version until a stable release exists.
- The "at least one release branch" validation counts only **release-type**
  branches (plain name, no range, no prerelease). Maintenance-pattern names
  like `1.x` do NOT count even when the branch exists — hence the `stable`
  placeholder branch (see `.releaserc.yml`;
  semantic-release/semantic-release#2503).
- Commit types drive releases, so commits must be honest about their type —
  no smuggling tooling changes into `docs:` commits.

## OKF / spec

- OKF requires **no** specific files; `index.md`/`log.md` are reserved
  names, not mandates. A frontmatter-less `README.md` inside a bundle root
  makes the bundle non-conformant — bundle root must never be repo root.
- The reference implementation's `type` validation applies Python
  truthiness (`type: 0` counts as missing) — adopted for interop parity
  (see decisions.md).
- PyYAML scalar resolution has sharp edges a port must match exactly:
  single-letter `n`/`N` are strings (not booleans), `0x`/`0b` are
  lowercase-only, floats need a dot in the mantissa AND a signed exponent.
  Differential testing against the live reference caught three mismatches.

## .NET / AOT

- YamlDotNet's node-level API (`YamlStream` + `Emitter`) is reflection-free,
  so AOT-safety never depends on its source generator. Zero IL warnings on a
  real `PublishAot` publish; 4 MB stripped binary.
- `IsAotCompatible` on a library only runs the analyzer — a real
  `PublishAot=true` publish of an executable is the only actual proof.
- `dotnet new gitignore` ships an older snapshot of GitHub's
  `VisualStudio.gitignore`; the upstream file can be newer.
- **PAX tar archives written by `System.Formats.Tar` are not reproducible**:
  every entry is preceded by an extended header named `./PaxHeaders.<pid>/.`
  (a constant, whatever the entry's path), so the archive embeds the writing
  process's pid. `TarReader` hides those entries, so a test written through
  the reader cannot see them, and a two-writes-in-one-process byte comparison
  passes. Use `TarEntryFormat.Gnu` — mtime in the header, no pid anywhere, and
  no 100-character path limit. GNU is not *entry*-free: a path over 100
  characters gets a preceding `././@LongLink` block, but that name is a
  constant and carries no host state, which is the property that matters.
  Ustar is reproducible but throws outright on a path over 100 characters it
  cannot split across its name and prefix fields. `GZipStream` is fine — its
  header carries MTIME 0 and no filename.
- **Do NOT set `AccessTime`/`ChangeTime` on a `GnuTarEntry`.** Unset they are
  already deterministic — `System.Formats.Tar` writes NUL bytes, which is
  what GNU tar itself writes for a non-incremental entry — and pinning them
  breaks readers without helping any: GNU puts atime/ctime at byte 345, which
  in ustar is where the `prefix` field begins, and CPython's `tarfile` joins
  `prefix` onto the entry name for every non-GNU-typed entry without checking
  the archive's magic first. Pinned, `tar -xzf` and libarchive read the
  archive correctly while the Python standard library extracts it into a
  directory named after the octal timestamp (`02263523000/bundles/…`).
  Reproducibility and interoperability are two claims: byte-comparing two of
  your own archives proves the first and says nothing about the second, so
  read one back with a *different* implementation.
- **`TarEntry.DataStream` is null for a zero-length file**, because tar stores
  one as a header with no data section. Reading null as "not a file" makes an
  empty file vanish from anything that reads the archive back — here
  `okf bundle --verify` reported the bundler's own tar.gz as *missing* a file
  it had just written, while the zip and directory shapes passed. Switch on
  `EntryType`, and read a null `DataStream` on a regular-file entry as
  `Stream.Null`.

## Testing

- Two vacuous-assertion cases shipped and were caught only by adversarial
  review (`Contains("0 errors")` matches `"10 errors"`; a test that couldn't
  distinguish a clean bundle from an empty directory). Hence the
  tests-must-fail-first rule in AGENTS.md and the Stryker.NET work item.
- Differential testing (running the Python reference as an oracle) is the
  strongest expectation source for a port — expected values are independent
  of the code under test by construction.
- Bundle walks must not descend directory symlinks: a symlink to an ancestor
  turned a 1-file bundle into 81 phantom files, bounded only by `PATH_MAX`.

## Process / agents

- Every `feat:` commit got an adversarial Opus review before push; each
  review found real defects the builder missed (symlink walk, marker
  detection trusting any line of a file, scalar-resolution mismatches).
  Review is not overhead here; it is where correctness came from.
- A stalled subagent can be resumed with its on-disk work intact — check
  `git status` first, then resume it with a summary of confirmed state.
- Parallel builders on separate branches independently created the same type
  name in `Okf.Core` (work items #4 and #7) with different
  meanings — a write-form renderer and a comparison type. File-level
  conflict-minimization cannot prevent this: neither branch's file existed in
  the other, so git had nothing to conflict on and the collision only surfaced
  at merge. Reviews caught it pre-merge and the comparison type became
  `OkfLifecycleInstant`. Mitigation for next time: reserve new type names in
  the prompt, or give each parallel builder its own namespace.
- Repo-local git identity may be an *agent* identity
  (`ringo.harrison+agent@gmail.com`) — anything deriving a human identity
  (e.g. `okf verify`) must read **global** git config, never local.
