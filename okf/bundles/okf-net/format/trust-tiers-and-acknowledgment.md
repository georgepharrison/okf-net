---
type: Concept
title: Trust Tiers and the Acknowledgment Model
description: Confidence in a concept is derived from generated and verified, never stored as a score or a status field.
tags: [okf, trust, verification, acknowledgment, lifecycle]
generated: { by: claude-fable/5, at: 2026-08-15T01:55:27-05:00 }
sources:
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/3fcbb9f828c2f23d109c855ee403c3a4c81f3a96/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
---

Two frontmatter families carry confidence, and the split between them is the
point: `generated` records **who wrote** the current content, `verified`
records **who confirmed** it. They are independent — content can change
without re-confirmation, and a fact can be re-confirmed without being
rewritten.[^okf-spec]

```yaml
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
verified:
  - { by: "human:ringo", at: 2026-08-20T09:00:00Z }
  - { by: "process:nightly-schema-check", at: 2026-08-21T02:00:00Z }
```

Both use the same actor convention: `<producer>/<version>` for a tool or
agent, `human:<id>` for a person, `process:<id>` for an automated process. A
single verifier may be written as a bare `{ by, at }` mapping with no list
dash, and a consumer must read that as a one-element list.

# The tier is derived, not stored

Three tiers, lowest to highest:

| Tier | Condition |
| --- | --- |
| `unverified` | no verification events |
| `machine-confirmed` | events exist, none from a `human:` actor |
| `human-reviewed` | at least one event from a `human:` actor |

Derivation reads `verified` and nothing else. Not `generated`, not `status`,
not `sources`. This is the same instinct behind the format's refusal to store
a credibility *score* on a source: a stored judgement is subjective,
unportable between consumers, and starts going stale the moment it is
written, whereas signals stay true and let each consumer judge for itself.

# Who may verify what

An agent may record a machine-confirmed verification. **The agent that
generated the content may not verify it** — self-verification is a
contradiction dressed as a signal, and okf-net reports any `verified[].by`
equal to `generated.by` as a warning.[^decisions] Human review is a separate
act: `okf verify` stamps `human:<id>`, taking the identity from `verify.actor`
in configuration and falling back to the *global* git email, deliberately
never the repository-local one, because a repository-local identity is so
often an agent's.

An agent writing content updates `generated.{by,at}` and nothing else.

# Acknowledgment needs no new field

A concept is **unacknowledged** when its `generated.at` is newer than the
latest `verified[].at`, or when it carries `status: draft`. That is the whole
definition, and it introduces no frontmatter key the specification does not
already have — a fourth field would be one more thing for two implementations
to disagree about.

One clause is implied by the other two and worth writing down: a concept an
agent generated and *nobody* has verified is unacknowledged, because there is
no `verified[].at` for `generated.at` to fail to beat. A concept a **person**
generated and nobody verified is not — the author is the acknowledgment. So a
vault written entirely by agents starts with every concept on the inbox, which
is accurate rather than alarming.

The workflow around it is deliberately mundane: `okf inbox` lists what is
unacknowledged, `okf verify` clears it, `log.md` records notable updates, and
a custodian running in CI opens a merge request. The merge request *is* the
inbox for machine-derived insight — reviewable, and ignorable, which matters
just as much. See [the custodian
model](../practices/custodian-model.md).

# What the two commands do

`okf inbox [path]` lists what is waiting across the resolved vault, grouped by
reason — **unacknowledged**, **stale**, **source drift** — with one row per
concept and every reason that applies on it. It is a report and exits 0
whatever it finds; `--fail-if-any` exits 1 for a caller that genuinely wants
the branch, and `--format json` emits the same rows as records for a custodian
to work from.

`okf verify <concept-path>...` appends `{ by: human:<id>, at: <now> }` to
`verified` and touches nothing else — not `generated`, not the body, not the
formatting of any line it did not add. The identity comes from `verify.actor`
in configuration, else the *global* git email. `--by <actor>` records a second
actor's machine confirmation instead, and either form refuses an actor equal
to `generated.by`.

The two are asymmetric on purpose. Verification clears the acknowledgment
reason and nothing else: a concept that is also `status: draft` stays on the
inbox after it is verified, until a person removes the marker. Staleness and
drift are cleared by re-checking the content against its source, which is
[the custodian's refresh
procedure](../practices/custodian-model.md) and not something a stamp can do.

# Staleness is a third axis

`stale_after` is lifecycle, not trust: a concept is stale once today is on or
after that date, regardless of who verified it or when. Staleness is reported
and never blocks a build by default, because an expired date is a prompt to
look again rather than evidence that anything is wrong.

Source drift is the same axis from the other end — a cited
`sources[].last_modified` later than `generated.at`, which says the ground
moved after the writing did. Both are reported by `okf lint` as configurable
diagnostics and by `okf inbox` as reasons a concept is waiting; the difference
is what the two commands are for. Lint asks whether the bundle is in good
order, so it reports one finding per problem. The inbox asks what a person
should look at next, so it reports one row per concept with every reason that
applies on it.

[^okf-spec]: Open Knowledge Format (OKF), version 0.2, §§5.2, 5.3, 5.4, 5.5, 7.
[^decisions]: okf-net — Architecture Decisions, §7 and Q6.
