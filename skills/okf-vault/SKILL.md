---
name: okf-vault
description: >
  Look up what a project already knows in its OKF vault and answer from it:
  orient by index, retrieve with `okf search`, open only the concepts you
  picked, and cite each one back with its trust tier. Use when asked what we
  know about a topic, and when asked what needs attention, which is
  `okf inbox`.
---

# okf-vault

A vault is read to answer two questions — *what do we already know about
this?* and *what is waiting on a person?* Both go wrong the same two ways:
answering from memory while the vault holds better, and answering from the
vault without saying which concept said it.

## Doctrine

**Orient by disclosure, retrieve by search, open what you pick.** Read
`index.md` to learn what a bundle holds, `okf search` to find candidates, and
the concept files search pointed at.

**The bundle is what you read.** Concepts under a bundle root are the
knowledge. `raw/` is the producer's evidence archive, it sits outside every
bundle root, and nothing indexes, searches, or serves it.

**Judge before you trust.** Every hit carries a trust tier and a stale flag,
so a hit can be judged before it is opened — and so your answer can carry that
judgement to the human.

## The interface

Three moves, on whichever surface the host gives you:

| Move | MCP tool | CLI |
| --- | --- | --- |
| Orient | `okf_list` | read the bundle's `index.md` |
| Retrieve | `okf_search` | `okf search <query>` |
| Open | `okf_read` | read the concept file |

One engine underneath: the tools resolve vaults and rank results by exactly
the CLI's rules, and `okf_search` returns the same array `okf search --json`
prints, from the same writer. Where the host exposes the tools, use them; the
steps below name the CLI, and the tool of the same move is that step.

`okf <command> --help` is the option lookup. It ships with the binary in front
of you, so it is right about flags even when this file is old.

Where a project documents its own way of running the CLI, that is the command
— in okf-net's own repository it is `mise run cli -- <command>`. With `okf`
absent from the PATH and no documented equivalent in the project, say so and
stop: a text scan of the directory is a different question with similar-looking
answers.

The layout it resolves against, walking up from the working directory:

```text
<project>/okf/
  bundles/<name>/  # bundle roots — the concepts you read
  raw/             # the producer's evidence, OUTSIDE every bundle root
```

Failing that, the personal vault at `OKF_HOME`, else `~/okf`.

## 1. Orient

```sh
okf search "<the topic>" --verbose
```

`--verbose` names the scope, the vault and the bundles it resolved, on stderr,
so one run answers *is there a vault here* and starts the retrieval.

Scope is the project's vault and nothing else by default, which is what makes a
query answer the same on every machine. When the project's vault does not hold
the answer and the human keeps knowledge elsewhere, widen it deliberately:
`--scope personal` for their own vault, `--scope registered` for the vaults
they registered, `--scope all` for both plus this project. Say which scope
produced an answer, because a result from outside the repository is not
something a teammate's `okf search` will reproduce.

For a question about the vault's shape rather than its content — *what does
this project document?* — read the bundle's `index.md` and then the `about.md`
of the branch it points at. There the tree is the answer.

**Done when** you can name the vault path and the bundles in scope, or you
have told the human there is no vault here.

## 2. Retrieve

Search terms are the words the *concept* would use — as few as name the thing,
often one. A question's own phrasing carries words no concept contains.

Terms are AND-ed, so each extra word narrows, and when nothing matches every
term the search falls back to matching any of them and says so. Filters narrow
a field you already know, inline or as the flags `--help` lists:

```sh
okf search "index drift tag:lint type:Reference" --limit 20
```

A filter compares the whole value, case-insensitively — `tag:lint-severity`
never matches a concept tagged `lint` — and it restricts which concepts may be
returned without contributing to the score. Raise `--limit` when you are
browsing a filter rather than answering a question.

Read the summary line under the results: it says how many concepts were
searched across how many bundles. Zero concepts searched is a resolution
problem to report, where zero results out of many concepts is an honest miss.

**Done when** you have a result list, and every hit you will not open has been
dismissed by its title and snippet.

## 3. Judge the hits

Each result ends in its markers: `[human-reviewed]`, `[unverified, stale]`.

- **Trust tier** records who confirmed the content. `unverified` — nobody yet.
  `machine-confirmed` — a second agent or process re-checked it against its
  source. `human-reviewed` — a person did.
- **Stale** means the concept has passed the `stale_after` date it set for
  itself: it asked to be re-checked by now.

Prefer human-reviewed and fresh. Where the only hit is unverified or stale it
is still the answer available, and it travels onward with that fact attached
(step 5).

A hit on an `about.md` is a signpost rather than an answer: it ranks because it
names its whole branch, and the concept it links to is what holds the content.
Follow it down and answer from there.

**Done when** the one to three concepts you are about to open were picked on
tier, staleness, and title together.

## 4. Open what you picked

Open those concept files and read them. A snippet is built to be judged rather
than quoted — the concept's own `description`, or a fragment around one
matched term — and the body is what you answer from.

**Done when** every claim in your answer comes from a concept body you opened.

## 5. Cite it back

Give the human the concept, not just the fact:

```text
Index drift is an error in this vault rather than a warning — okf-net,
`toolset/index-generation-and-drift.md`, "Index Generation and Drift"
(unverified).
```

That reference is everything the result line printed after `bundles/`: the
bundle's name, then the path inside it, which is the pair that identifies a
concept anywhere the bundle travels (`bundleName` and `path` in `--json`). The
whole path search printed is where the file sits relative to where you ran the
command, so hand that one over as well when the human is standing where you
are.

Where you leaned on a hit that was unverified or stale, mark it in the
sentence that uses it. "The vault says X, unverified" is something a person
can act on; a confident X is not.

**Done when** every claim you attribute to the vault names its concept and its
tier, and each stale or unverified one is marked where it is used.

## When the search misses

Take these in order and stop at the first that answers:

1. **Read the fallback.** Where the output says it matched any of the terms
   rather than all, the query already widened as far as its words reach — the
   whole list *is* that widening, every row of it worth judging, the top one
   included. Removing a term from here searches for less than the fallback
   just searched for.
2. **Reword.** Matching is on whole tokens, so `returns` and `return` are two
   different words and only one of them is in the vault. Try the form a
   concept would put in its own title, and a synonym for the idea.
3. **Browse the tree.** The bundle's `index.md`, then the `index.md` or
   `about.md` of the likeliest branch. A filter alone browses too:
   `okf search "tag:lint" --limit 20`. A concept whose title and description
   never use your words is reachable this way.
4. **Say it plainly.** *The vault has nothing on X* is a useful answer, and it
   reads as a gap worth filling rather than as a failure.

**Done when** you have either named the concepts that answer the question, or
told the human the vault does not cover it and named the terms you tried and
the branches you browsed.

## What needs attention

```sh
okf inbox
```

Rows are the concepts waiting on a person, grouped by reason, and a concept
appears under every reason that applies. `--help` states each reason exactly;
what the groups are worth saying to a human:

- **Unacknowledged** — content generated with no verification newer than it,
  or a concept still marked `status: draft`. A whole young vault sits here,
  which is a fact about its age rather than a queue of problems.
- **Stale** — the concept asked to be re-checked by a date now past.
- **Source drift** — a cited source moved after the concept was written, so
  the concept may be describing a version that is gone.

A group with no rows prints no heading, and the closing line counts every
reason including those — read it for the shape of the whole vault, then the
rows themselves for what touches the human's current work. Clearing the last
two groups is a custodian's job: fetching what moved and drafting the update
is **okf-custodian**'s refresh procedure.

**Done when** your reply carries the closing line's count for all three
reasons, and names the rows that bear on the human's current work.

## Guardrails

- **Reading is this skill; writing is okf-capture's.** When a session turns up
  something worth keeping — a new fact, a correction to a concept you read, a
  source worth archiving — hand off to **okf-capture**, which searches first,
  captures the evidence, and writes a conformant concept.
- **`okf verify` is the human's word, spoken on request.** It stamps
  `human:<id>` from their configured verifying identity — `verify.actor`, else
  their global git email — on the concepts they name, when they ask you to run
  it. The command refuses an actor equal to a concept's `generated.by`, and
  `okf lint` reports the pairing (`OKF0201`): an actor confirming its own
  generation is a contradiction dressed as a signal.
