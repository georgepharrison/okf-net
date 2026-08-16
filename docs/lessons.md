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
- **A "protected" CI variable is invisible to every unprotected branch, and
  the job that needs it fails at the end rather than the start.** Adding a
  second release branch is therefore two changes, not one: the pipeline rule,
  and the branch's protection status. `glab variable list` shows the
  `PROTECTED` column without revealing any value, which is the cheap way to
  check before wiring a job onto a new branch.

## semantic-release

- **Pin the release plugins, exactly.** `npm install -g semantic-release
  @semantic-release/… conventional-changelog-conventionalcommits` resolves
  five independently versioned packages fresh on every pipeline, and a
  release job is the one job whose output nobody reads until it is already
  published. Work item #52: every release the project had ever cut — all 34,
  v1.0.0-rc.1 through v1.0.0-rc.34 — shipped a GitLab Release whose
  description was the `## version (date)` header and nothing else.
- **The root cause was a writer-version mismatch across a runtime name
  lookup.** `@semantic-release/release-notes-generator@14` renders through
  `conventional-changelog-writer@^8`, which takes Handlebars template
  *strings* under a `mainTemplate` key.
  `conventional-changelog-conventionalcommits@10` emits the writer-9 shape
  instead: template *functions* (from `@conventional-changelog/template`)
  under a `template` key. Nothing catches this, because
  `preset: conventionalcommits` is resolved by NAME at run time and no
  dependency edge constrains its major. The failure is silent rather than
  fatal: Handlebars accepts a function as a partial, so the preset's
  `headerPartial` still rendered the header, `template` was ignored in
  favour of writer 8's default `mainTemplate`, and every commit section
  rendered empty. Pinning the preset to `9.3.1` restores sectioned notes;
  the pin lives in the `release` job in `.gitlab-ci.yml`.
- **Reproduce release notes without releasing anything.** `git clone
  --mirror` the repo into a scratch dir, delete the tag you want to release
  *past*, and point the working clone at the mirror with
  `git config url.<mirror-path>.insteadOf <real-remote-url>`. semantic-release
  then still reports `repositoryUrl` as the real remote — so the compare and
  commit links in the generated notes are the real ones — while every fetch
  and push-dry-run it performs lands on the local mirror. `--dry-run --no-ci`
  against that prints the exact notes the GitLab Release will carry. Mirror
  the REMOTE, not a local clone: semantic-release reads each prerelease
  tag's channel from `refs/notes/semantic-release-*`, which an ordinary
  clone never fetches, and without those notes it finds no previous release
  and renders the whole history instead of the one tag's commits.
- **`presetConfig.types` REPLACES the preset's default type list, it does not
  extend it.** A type missing from `.releaserc.yml` is a type missing from the
  notes, with no warning — `test:`, `build:` and `style:` commits are absent
  from okf-net's release notes for this reason, deliberately.
- On a prerelease branch with no stable release, every releasable commit
  produces the next `1.0.0-rc.N` — bump *type* (feat/fix/docs) does not
  change the target version until a stable release exists.
- The "at least one release branch" validation counts only **release-type**
  branches (plain name, no range, no prerelease). Maintenance-pattern names
  like `1.x` do NOT count even when the branch exists — which is why a
  prerelease-only config needs a placeholder release branch (okf-net carried
  one called `stable` until the 1.0.0 flip made `main` itself the release
  branch; see `.releaserc.yml`; semantic-release/semantic-release#2503).
- **`prerelease: rc` sets the version identifier; the CHANNEL still defaults
  to the branch name.** A branch configured `{name: dev, prerelease: rc}`
  logs "Published release 1.0.1-rc.1 on **dev** channel" — the tag is
  `v1.0.1-rc.1` as intended, and the channel is a separate field (a git-note
  label and an npm dist-tag) that takes the branch's name unless `channel` is
  set explicitly. The log line reads like a misconfiguration and is not one.
- **When the prerelease channel moves to a new branch, that branch must
  contain the stable tag before anything merges into it.** A prerelease branch
  reads its last release from tags on its own history *and its own channel*, so
  rc tags cut while a different branch carried the channel do not count. A `dev`
  left behind the new `v1.0.0` therefore reports "There is no previous release"
  and computes `1.0.0-rc.1` — a tag that already exists — and the release job
  fails on the push. Fast-forwarding `dev` onto the stable tag first makes the
  same commit compute `1.0.1-rc.1`. Both measured in a mirror (work item #10).
- **A dry run can simulate a future the remote has not reached.** The mirror
  is writable and nobody is watching it, so the state you want to test can be
  *manufactured* there: work item #10 checked that the `rc` channel still
  works after a stable release exists by tagging a fake `v1.0.0` in the
  mirror, branching `dev` off it, adding one `fix:` commit, and pushing that
  to the mirror path (never `origin`). semantic-release computed
  `1.0.1-rc.1`. Without the fake tag no dry run can reach that state, because
  the state does not exist until the flip has already happened.
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

- **A local acceptance matrix can silently halve.** `tests/install-sh/run.sh`
  runs the installer under `sh` and, *if it is installed*, under `dash` — the
  shell that catches a bashism `sh` would forgive. Arch does not ship `dash`,
  so a local run exercises one shell and reports a green that means half of
  what a CI green means. CI has both (the `test-install` job fails outright if
  `dash` is missing, rather than skipping the lane). The harness now prints
  `shells under test: …` before the first case, so the halving is visible in
  the output instead of inferred from a suspiciously round assertion count.
  Any harness whose coverage depends on what is installed should say what it
  actually ran.
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

## Operations / local environment

- **Docker's default address pools exhaust `172.16/12` at sixteen networks and
  then roll into `192.168.0.0/16` — straight through a home LAN.** Each user
  network takes a whole `/16` by default, so the sixteenth compose stack on a
  host lands on `192.168.x.x` and the host starts routing the LAN's own subnet
  into a bridge. Two things make it worse than a one-off: the networks
  **persist across reboots**, so the collision comes back after every restart
  until they are removed, and Tailscale keeps working throughout (it is on
  `100.x`), so the box stays reachable and the failure looks like "the LAN is
  broken" rather than "Docker took the LAN". Mitigations, both needed: set
  `default-address-pools` in `/etc/docker/daemon.json` on **every** Docker host
  (tycho uses `10.60.0.0/16` carved into `/24`s, which is 256 networks in the
  space one default network used to take), and pin an explicit subnet on any
  stack whose compose file you do not control or cannot inspect (tycho pins
  those in `10.61+`). Auditing after the fact is `docker network ls` plus
  `docker network inspect` — the pool setting only governs networks created
  *after* it, so existing ones must be recreated.
- **Do not run a Windows-installer harness under `pwsh` on a Linux desktop
  without sealing it off first.** `install.ps1`'s error and success paths do
  things a browser is registered for, and under `pwsh` on Linux those resolve
  through `xdg-open` — which does not care that it was invoked from a test:
  it reaches the running desktop session and opens tabs and applications while
  the suite runs. Run such a harness with `BROWSER=/bin/true` and no `DISPLAY`
  in its environment, and shim the target binary as an **executable stub** on
  `PATH` rather than letting the script find a real one. The general rule: a
  test that exercises a script written for another OS inherits that script's
  side effects on *this* one, and the desktop session is the blast radius.
