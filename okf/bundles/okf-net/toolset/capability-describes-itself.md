---
type: Concept
title: A Capability Describes Itself
description: Why this bundle carries a concept for a command written in the same change, how its generation stamp proves it was written by the tool it documents, and what that self-run demonstrates and does not.
tags: [okf-net, dogfood, meta, selfdoc, trust, verification, provenance, cli]
sources:
  - id: issue-81
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/81
    title: "Dogfood: the capability documents itself"
    author: "human:ringo"
    last_modified: 2026-09-14
  - id: issue-71
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/71
    title: "Add `okf candidates`: enumerate the concepts with zero verification history"
    author: "human:ringo"
    last_modified: 2026-09-13
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/dev/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-09-14
generated: { by: "pi/qwen3.8-flash-next", at: 2026-09-14T07:50:56Z }
---

The `okf candidates` feature arrived with a work item that asked for something stranger than
documentation: the repository's own bundle had to gain a concept describing the command **in
the change that shipped it**, and that concept had to show up in the command's own output as
an unverified candidate.[^issue-81] It is the same self-hosting claim the vault makes in its
README, run against the change rather than against a fixture: the tool is exercised on its own
work, so the demonstration cannot be staged.

# The concept is the test

**A fixture proves that a scanner reads bytes; a self-run proves that the tool reaches the
work it just did.**[^issue-71] The bundle already held a concept about candidate enumeration —
[candidate enumeration and the completeness contract](../trust/candidates-and-completeness.md)
holds the eligibility contract and the exit-code semantics in prose. What a fixture could not
show is that a concept *this repository wrote in order to ship the command* is itself in scope
for the command. That is the assertion the acceptance criteria are built on, and it is the kind
that goes quietly false: a scanner that skipped the directory the concept happened to land in
would still pass every fixture in the suite.

So the concept lives in `toolset/`, beside the other concepts about how okf-net is built, and
the inventory lists it. The eligibility question it answers is the narrow one — **is there at
least one structurally recognizable verification event in this file's `verified` block?** — and
the answer for every concept in this bundle is no, because nobody has ever run `okf verify`
here.[^issue-71] [The eligibility contract and the exit-code semantics are the trust domain's
to state](../trust/candidates-and-completeness.md); what this concept holds is what the
self-run proves and what it leaves open.

# The stamp is written, not typed

`generated` on this concept was not typed by the author who wrote the prose. It was written by
the verb it documents:

```sh
mise run cli -- generated stamp okf/bundles/okf-net/toolset/capability-describes-itself.md \
  --by <producer>/<version>
```

That is a convention the vault holds everywhere, and here it is load-bearing rather than tidy.
A hand-typed timestamp would prove only that somebody could type one; the claim this concept
makes about its own freshness — and every stale and drift check downstream of
`generated.at`[^decisions] — rests on the instant having come from the tool. `okf verify`
refuses an actor equal to a concept's `generated.by`, and `okf lint` reports the pairing as
`OKF0201`, so the actor written here also fixes what a verifier is allowed to say later about
this file.

**The stamp is the reason the concept is on the inbox and not the reason it is on the
inventory.** Those are different lists, and the distinction is the whole reason the feature
exists: a fresh stamp makes a concept unacknowledged and therefore listed by `okf inbox`,
while `okf candidates` ignores it entirely, because a `generated` stamp says who wrote a
concept and never that anybody checked it.

# Verification history and trust tier are separate records

The concept carries **no `verified` key at all** — not an empty list, not a stub, nothing — and
that absence is the fact the enumeration reads. It is also the fact the trust tier reads, and
the two readings are not the same record and must not be collapsed into one.

Verification history is a list of events, each naming an actor and optionally an instant, and
the eligibility verdict asks only whether that list holds anything recognizable. The trust tier
derives from the same block but answers a different question — *who* vouches — and lands on
`unverified` here because there is nothing to derive from.[^decisions] The two agree about this
file by accident rather than by design. A `verified: {}` block derives `machine-confirmed` to
§5.3 while the scanner refuses to classify the concept at all. **That asymmetry is designed and
not a defect**.[^issue-71] What keeps a disagreeing tier from being a hole is designed too, and
is not luck: such a concept is named on the incomplete list either way — so a tier can mislead a
reader about who vouched, and still cannot hide a concept from review.

**This concept does not claim that the tier and the enumeration agree, and it does not claim
that they will be reconciled.** Issue #84 owns that question; until it decides, one surface
that reconciled the two would be a second answer to it.

# What the self-run demonstrates, and what it does not

Run over this vault, the enumeration names this concept and exits 0:

```text
okf/bundles/okf-net/toolset/capability-describes-itself.md  A Capability Describes Itself  [unverified] (Concept)
    no `verified` key — nobody has ever stood behind this content
```

Exit 0 is the point of that line, not an afterthought. The command is an inventory and not a
gate on volume, so a vault in which every single concept is unreviewed is a **clean run**: the
exit code reports whether the enumeration was trustworthy, and nothing about its length moves
it.[^issue-71] A gate that went red on a full backlog could not be used as the scan step of a
verification pipeline, which is the consumer the whole contract was written for.

What it does not demonstrate is the half a reader is most likely to assume. Appearing on this
list is not evidence that anybody looked at this concept, and it is not a judgement on the
prose: the command verifies nothing, reviews nothing, stamps nothing, and writes nothing. The
list says only that nobody has yet written themselves down behind it — which for the entire
bundle is accurate rather than modest, since nothing here has been independently confirmed
against its sources by a second actor.

The list also stays the same length until somebody changes it by other means. Adding a
`verified` event by hand would move the concept off the inventory and would be exactly the
wrong fix, because the event is a claim about a person, not a field to fill in. The concept
leaves the inventory when someone reads it and runs `okf verify` — a second actor's act, on
request, with an actor that is not the one that generated the file.

[^issue-81]: Dogfood: the capability documents itself.

[^issue-71]: Add `okf candidates`: enumerate the concepts with zero verification history.

[^decisions]: okf-net — Architecture Decisions, the candidate-enumeration and trust-tier entries.
