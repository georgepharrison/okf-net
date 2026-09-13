---
type: Concept
title: Candidate Enumeration and the Completeness Contract
description: The eligibility test behind `okf candidates`, why it is not the trust tier's question, and the exit-code contract that reports an incomplete inventory instead of a shorter list
tags: [okf-net, trust, verification, lifecycle, determinism, cli]
generated: { by: openai-codex/gpt-5.6-luna, at: 2026-09-13T00:00:00Z }
sources:
  - id: issue-71
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/71
    title: "Add `okf candidates`: enumerate the concepts with zero verification history"
    author: "human:ringo"
    last_modified: 2026-09-13
  - id: issue-75
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/75
    title: Expose okf candidates as a verb
    author: "human:ringo"
    last_modified: 2026-09-13
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/dev/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-09-13
---

Two concepts in an okf vault can look identical to a tool and mean opposite things. One has
been read and signed off; the other has never been looked at. `okf candidates` separates them,
and the way it does so is a rule worth writing down because it is *not* the rule the trust tier
uses.[^issue-71]

# The eligibility question is not the tier's question

**The tier asks *who* vouches; the candidate verdict asks *whether anybody wrote themselves
down at all*.**[^decisions] Keeping those two questions apart is the whole design, and
conflating them produces a tool that skips concepts.

| `verified` block | Candidate? | Why |
| --- | --- | --- |
| absent, `null`, `~`, `[]` | **yes** | Provable absence — the only thing that qualifies |
| `{ by: human:ahormati, at: yesterday }` | no | Someone signed it; a garbage timestamp is still a somebody |
| `{ by: "human:" }` | no | A thin identifier is the actor grammar's problem, not this verdict's |
| `{}` | **quarantined** | A stub naming nobody — must not be blessed as verified |
| `ahormati`, `42`, `true` | **quarantined** | A scalar claiming verification is not a structure of events |
| `[{ by: human:x }, {}]` | **quarantined** | One malformed item in a list hides the concept |

A sequence counts as history only when **every** item is a recognizable event — a mapping with
a non-empty scalar author. "At least one event is signed" was rejected deliberately: it lets a
garbage entry ride along inside an otherwise-readable list, which is precisely how a concept
escapes review.[^issue-71]

# Why the shared normalizer could not be reused

`OkfDocument.NormalizeVerified` is the hinge for the trust tier, lint, search, the inbox, the
site, and the stamping safety check, and every one of its tolerances is pinned by a test. It
reads `verified: "ahormati"` as **zero** events and `verified: {}` as **one**.[^issue-71]

Built on that reading, a verifier would make both errors at once: it would re-review a concept
somebody tried to mark verified, and **silently skip** one nobody ever vouched for. The second
is unforgivable — the entire point of the enumeration is that nothing escapes review because a
tool decided it was uninteresting.

So the verdict reads the same bytes a second way, narrowly, and leaves the hinge alone.
`OkfVerificationHistory.Verdict` answers three-valued — `Absent` / `Present` / `Unreadable` —
and `OkfVerificationStamp` reads the same block for *what it says* (who, when, how many items
could not be read) while consulting the verdict for whether the block is readable at all.
**Two shape tests would be two answers to one question**, and a report row that printed an
event the verdict refuses to count would contradict the quarantine list printed beside it.

A consequence worth stating plainly: a quarantined concept may print a trust tier that
disagrees with its own verdict. `verified: {}` reads `machine-confirmed` to §5.3 while the
scanner refuses to classify it. That is intended — the concept is named on the incomplete list
either way, so a disagreeing tier can never hide it from review.

# Complete, or it says so

**The two channels are the contract.** Standard output carries the inventory and nothing else,
in both formats. Quarantine notices go to standard error, in both formats. Exit 1 means *"that
list is not the whole truth."*[^issue-75]

```text
0  the enumeration completed, whatever its length
1  at least one concept in scope could not be classified
2  usage or environment failure
```

A machine consumer never infers completeness from a field — it is already reading `$?`.
Quarantined concepts are deliberately **absent from the JSON array**: mixing two kinds of record
into one array would force every consumer to branch on a discriminator to find the list it
asked for.

**An inventory, not a gate on volume.** Forty candidates is exit 0, and there is no
`--fail-if-any` here even though `okf inbox` has one. The length of this list *is* the answer;
only an unclassified concept moves the exit code. That is what lets a verification pipeline's
scan step run without failing on a backlog.[^issue-75]

# Three small rules that are easy to get wrong

**The "why" sentence belongs in the library, not the renderer.** `OkfVerificationStamp.WhyCandidate`
names the shape the file actually holds — no key, `null`, or `[]` — because "unverified" alone
does not tell the person fixing it which edit to make, and AD-6 puts readings of a document in
`Okf.Core`. Reading it through `FrontmatterValues.Scalar` would have collapsed all three into
one sentence: that helper applies §11 truthiness, which reads a null scalar and an empty
sequence as "no value". The distinction is the sentence's entire purpose.

**A summary prefix of its own.** The line begins `Scanned`, not `Checked`, `Generated` or
`Found` — the shared test harness already assigns those to lint, index and search, and reusing
one would make the harness attribute this verb's summary to a different command.[^issue-75]

**Files whose frontmatter does not parse are counted, not yet quarantined.** The summary carries
them as a parenthetical. That is a deliberate intermediate state: the reason exists in the
enumeration but is not populated yet, and until it is, the count is the disclosure that a broken
vault is not a clean one.

# What this is not

`okf candidates` is **not** `okf inbox`. The inbox lists concepts waiting on a *person*, which
makes it a superset in one direction (a concept verified last month and regenerated today is on
the inbox and has history) and a strict subset in the other (a hand-written concept with no
`generated` stamp is deliberately never listed there). It is a triage list, and it cannot be
asked for "all of them".[^issue-71]

It is not `okf search` either: search reports a tier, but it is ranked retrieval with a result
limit, and paging it to approximate "all" is a scanner reimplemented by whoever needed the
answer — bundle discovery, frontmatter parsing, `verified` normalization and reserved-file
semantics all over again.

Draft concepts, stale concepts and per-directory `about.md` files are all included: they are
concepts, and a concept nobody verified is a candidate whatever else is true of it. Generated
`index.md` and `log.md` never appear, because the shared concept walk does not call them
concepts.

The command writes nothing, launches no process, and touches no network.
