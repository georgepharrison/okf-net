---
type: Concept
title: The Custodian Model
description: A custodian maintains a bundle from beside it, never inside it, and surfaces machine-derived insight for review rather than landing it silently.
tags: [okf-net, custodian, agents, maintenance, ci]
generated: { by: claude-fable/5, at: 2026-08-15T08:30:00Z }
sources:
  - id: agent-skills
    resource: /references/agent-skills.md
    title: Equipping agents for the real world with Agent Skills
    author: "Barry Zhang, Keith Lazuka, Mahesh Murag (Anthropic)"
    last_modified: 2025-12-18
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: prd
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/prd.md
    title: okf-net — Product Requirements
    author: "human:ringo"
    last_modified: 2026-08-14
---

A **custodian** is the agent or process that maintains a bundle: discovery,
enrichment, prose writing, index regeneration, and the log. It is
producer-side machinery, triggered by git hooks and CI. A **bundler** is its
counterpart on the way out, packaging a bundle for distribution so a consumer
receives only readable markdown.[^decisions] It is built: `okf bundle` ships
`bundles/` and nothing else, and what it does with the links that leave a
packaged bundle is [bundling and
distribution](../toolset/bundling-and-distribution.md).

Consumers are **consume-only**. A bundle is readable markdown, and reading it
never requires executing anything — which is the single constraint that makes
everything else about the custodian a producer's private business.

# Where the machinery lives

Beside the bundle, never inside it: `<project>/okf/custodian/` holds the
recipe configuration, the prompts, and the scripts, and no distribution ever
carries it — not because the bundler strips it, but because the packaging walk
starts at `bundles/` and never reaches it. The skills themselves are
*referenced* from
the recipe rather than copied into it — a vendored copy is a copy that
drifts.

The shared *toolset* — this repository — is referenced by version and never
vendored per project. Vendoring a copy of the tooling into every project is
how eight projects end up on six versions of the same linter with nobody able
to say which behaviour is current.

In-bundle scripts, where they exist at all, are Python: they must run in
arbitrary consumer and CI environments, and Python is the one interpreter
that is reliably already there. **Prose instructions and small deterministic
scripts may live in a bundle; intelligence lives outside it.**

# Prose versus structured

The division of labour that the whole model rests on:

- **Frontmatter** is the few machine-queryable fields. Code operates here.
- **The markdown body** is prose for humans and language models. Agents
  operate here.

A custodian is therefore not a code generator with better manners. It writes
the layer that code cannot check and leaves the layer code *can* check to the
tools — which is why `okf index` regenerates indexes rather than the
custodian hand-editing them, and why [drift is a lint
rule](../toolset/index-generation-and-drift.md) rather than a matter of
custodial discipline.

# Surfacing, not landing

Machine-derived insight is **offered for review, never landed silently**. The
chain is deliberately built out of primitives the format and the host forge
already provide — no new frontmatter field, no bespoke review queue:

1. The custodian writes or updates a concept and stamps
   `generated.{by,at}` — and nothing else. It never writes `verified` for its
   own output, because [an actor cannot verify
   itself](../format/trust-tiers-and-acknowledgment.md).
2. Uncertain output carries `status: draft`, which makes the concept
   unacknowledged by definition.
3. `okf inbox` lists everything waiting on a person — unacknowledged, stale,
   or citing a source that moved — grouped by reason, one row per concept.
   `okf verify <concept-path>` clears the first of those with a human stamp.
4. A custodian running in CI opens a merge request. **The merge request is
   the inbox** for machine-derived insight — reviewable, and, importantly,
   ignorable.
5. `log.md` records notable updates, newest first.

# The staleness-refresh loop

The same chain with a different trigger. When `okf inbox` lists a concept as
stale or drifted, the custodian fetches what changed — release notes from the
pinned version through to current for a version-pinned source, the current
content for a living URL — drafts an updated body carrying an explicit *what
changed since the old pin* section and its impact on the concepts that cite
this one, sets `status: draft`, restamps `generated`, moves `stale_after` out
**in the draft**, and surfaces the result as a merge request.

It never lands. Automated content refresh is an explicit non-goal:[^prd] the
custodian's product is a draft and an explanation, and the judgement about
whether the draft is right is the part a person is for. Landing is two acts,
not one — remove `status: draft`, then `okf verify` — because a verification
on a document still marked draft leaves it on the inbox, which is the marker
working as intended.

The procedure lives in `skills/okf-custodian/SKILL.md`, where the agent that
runs it will read it. There is no refresh script beside it, deliberately:
`okf inbox --format json` is the machine interface, and a second program that
re-derived the same rows in Python would be a copy of the thing to keep in
step with.

Of the five steps of the chain, four are now mechanical. `okf inbox` derives
the acknowledgment, staleness, and drift state; `okf verify` stamps; `okf lint`
reports staleness (`OKF0202`) and drift (`OKF0203`) against the bundle's
conformance; `log.md` is checked for ordering. The fetch-and-draft step in the
middle is an agent's work and stays that way.

# Search before create

The capture skill searches the resolved bundles *before* writing anything, and
either extends the concept it found or states why a new one is warranted. A
knowledge base that cannot resist adding a fourth document about the same
thing decays into a search problem, and then into an abandoned directory.

# The two skills

The custodian is instantiated by two prose skills shipped in the toolset's
`skills/` directory, host-neutral and calling the CLI rather than
reimplementing anything:[^prd]

- **`okf-capture`** turns something just learned into a concept: search first,
  apply [the capture-versus-cite
  test](../format/provenance-capture-vs-cite.md), drop what cannot defend
  itself into `raw/` with an entry in the capture manifest
  (`okf/raw/manifest.json`), and stamp `generated`.
- **`okf-custodian`** maintains the bundle: ingest a `raw/` item into an
  ordinary `references/` concept, enrich prose, regenerate indexes, clear
  lint, and append to `log.md`.

Both state the same doctrine — orient by disclosure, retrieve by search, open
what you pick; `raw/` is evidence and the bundle is knowledge — which is also
what `okf mcp` tells a client in its tool descriptions.

Each is a directory holding a `SKILL.md` whose frontmatter carries a `name`
and a `description` and nothing else. That shape is not okf-net's invention:
it is the published Agent Skills format, where the two keys are pre-loaded
into an agent's system prompt at startup and the body is read only once the
agent judges the skill relevant.[^agent-skills] Staying inside it costs
nothing and means the skills load in a host that already knows the format,
so nothing host-specific appears in either file — no tool names, no
`allowed-tools`, no slash commands.

The rhyme with the format is worth naming, because it is why the two sit
together comfortably. A skill discloses progressively — metadata, then body,
then bundled files — for exactly the reason a bundle does: an agent with a
filesystem should read the branch it needs and not the tree. `index.md` and
`description` are this bundle's `name`-and-`description` layer, and *orient by
disclosure* is the same instruction pointed at a vault instead of a skill
directory.

# Status on this bundle: active

The custodian is instantiated on this vault as of *Dogfood v2*.[^prd] What
that means concretely, and only what it means:

| Where | What runs | Written by |
| --- | --- | --- |
| `okf/custodian/recipe.json` | Names the two skills by repo path, the search seeds an enrichment pass starts from, the exact commands, and the triggers. | — |
| `pre-commit` hook | `markdownlint-cli2` on staged markdown; `check-manifest.py` when anything under `okf/raw/` is staged. | Nothing. Both report. |
| CI `dogfood` job | `okf lint okf/`, `okf index --check`, and `okf/custodian/check-manifest.py`. | Nothing. All three report. |
| CI `custodian-inbox` job | `okf inbox okf/`, on a schedule, publishing the JSON as an artifact. | Nothing. It surfaces and exits 0 whatever it finds. |
| A session with the skills | Capture, ingestion, enrichment, refresh drafts, indexes, `log.md`. | A person, deliberately. |

`okf/raw/` exists and holds its first capture, ingested into
[`references/`](../references/about.md) and closed in the manifest. Two gates
watch it, and they overlap on exactly one finding. `OKF0310` re-hashes every
ingested item against the `sha256` its entry records — the linter's one
vault-scoped rule, because `raw/` sits outside every bundle root and a rule
about it cannot be scoped like the others (see [the lint severity
model](../toolset/lint-severity-model.md)). `check-manifest.py` keeps the
rest, which is most of it: the id grammar, the timestamp forms, the
flat/packet layout, files in `raw/` no entry claims, ingestion pointers
resolving to concepts that exist, and a manifest that will not parse at all —
which the rule stays silent about on purpose, because a check that cannot read
the record cannot claim the artifact changed. Neither ever repairs anything.

What is **not** running, stated plainly because a custodian that overstates
itself is worse than none:

- **No agent on a schedule.** The `custodian-inbox` job *reports* on a
  schedule; nothing fetches release notes and nothing opens a merge request on
  its own. The refresh procedure is a session a person starts, and the job
  exists to tell them there is one worth starting.
- **No verification.** Every concept here remains `unverified` — no `verified`
  events at all, so the bundle sits at the lowest trust tier by construction
  and every concept in it is on the inbox. That is accuracy, not modesty: an
  actor cannot verify its own work, and no second actor has come through. The
  first `okf verify` on this bundle is a person's to run.
- **Enrichment is human-initiated.** The gates are mechanical; the writing
  they gate begins when someone opens a session and loads a skill.

[^decisions]: okf-net — Architecture Decisions, §1, §3, §7.
[^prd]: okf-net — Product Requirements, §2.4 and ACC-5.
[^agent-skills]: Equipping agents for the real world with Agent Skills, "The anatomy of a skill".
