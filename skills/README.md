# Agent skills

Three skills for working an OKF knowledge vault, one per job, all three
standing on the same sentence:

> **Orient by disclosure, retrieve by search, open what you pick.** Read
> `index.md` to learn what a bundle holds, `okf search` to find candidates,
> and the concept files search pointed at.

| Skill | Fires when |
| --- | --- |
| [`okf-capture`](okf-capture/SKILL.md) | something just learned is worth keeping — *remember this*, *write this up* — or a link, PDF, or talk needs to outlive its URL |
| [`okf-custodian`](okf-custodian/SKILL.md) | a bundle needs maintaining — captures waiting in `raw/`, index drift, stale concepts, prose to enrich |
| [`okf-vault`](okf-vault/SKILL.md) | a question could be answered from the vault — *what do we know about X* — or the human asks what needs attention |

Getting knowledge in is `okf-capture`'s; keeping it alive is
`okf-custodian`'s; getting it back out is `okf-vault`'s. The two producers
hand off to each other — capture drops the artifact, the custodian ingests it
— and `okf-vault` hands back to both at its write boundary, so a reading
session that turns up something worth keeping arrives at the right skill.

## Getting them onto a machine

The three files here are **embedded in the `okf` binary**, so a release of the
toolset carries the skills it was built with and installing them needs no
network:

```sh
okf skills install                    # okf's own copy, plus any host already present
okf skills list                       # what this binary carries
okf skills path okf-capture           # where a skill landed
```

`install.sh` and `install.ps1` run `okf skills install` for you once the binary
is in place (`OKF_SKIP_SKILLS=1` opts out). With no `--host` it writes:

| Target | Location |
| --- | --- |
| okf's own copy | `$XDG_DATA_HOME/okf/skills` (else `~/.local/share/okf/skills`; `%LOCALAPPDATA%\okf\skills` on Windows) |
| Claude Code | `~/.claude/skills`, only when `~/.claude` already exists |
| pi | `~/.pi/agent/skills`, only when `~/.pi/agent` already exists |

`--host claude|pi|generic|all` names a target explicitly, `--scope project`
puts it beside the project instead of in your home, and `--dir <path>` writes
anywhere — including a project's own `skills/`, which is what a repository that
would rather vendor them commits. A file you have edited is reported
`skipped (modified)` and left alone unless you pass `--force`.

In this repository the files here *are* the installed copy: `okf/custodian/recipe.json`
points at `skills/<name>/SKILL.md`, and `okf init` writes that same path into
any project that has one. A project that has none gets the instruction
`okf skills path <name>` in its recipe instead, because an absolute path in a
committed file resolves for exactly one person.

## Being found

Installing a skill puts the file on disk; it does nothing to make an agent
reach for it in a project that has never used okf before. `okf init` and `okf
skills install --scope project` close that gap by writing a marker-fenced
context-pointer block into the project's `AGENTS.md` (plus a one-line
`CLAUDE.md` naming it) — the one always-loaded line that tells a session this
project keeps its knowledge in a vault, and names the single trigger that
reaches each skill above. `--no-agents-md` opts out; see decisions.md's *the
AGENTS.md context pointer* entry for the wording rationale.
