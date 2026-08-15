---
type: Playbook
title: Tagging Discipline
description: The bundle's tag vocabulary, what each facet is for, and how a new tag gets added — in the same change as the concept that needs it.
tags: [okf-net, tags, governance, lint, conventions]
generated: { by: claude-fable/5, at: 2026-08-15T00:17:55-05:00 }
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

Tags are the format's cross-cutting axis: the directory tree carries the
subject, frontmatter `type` carries the kind of document, and `tags` carry
everything that cuts across both.[^okf-spec] A tag earns its place by joining
concepts that the tree has *separated* — a reader who wants everything about
provenance should not have to know that it lives in three directories.

This bundle's vocabulary is **closed**. It is written down in `lint.tagRegistry`
in [`okf/okf.json`](../toolset/vault-registry-and-config.md), and `okf lint`
reports any tag that is not in it. This page is that registry's human face: the
registry says which strings are legal, and this page says what they mean and
how the list changes.

# The rules

1. **Tag from the registry, spelled its way.** The comparison is exact —
   `okf-net` is not `okfnet`, and a plural is a different tag from its
   singular. A tag absent from the registry is reported as `OKF0305`.
2. **Every concept carries tags.** A concept with none is trivially compliant
   with any registry, which is why the companion rule `OKF0304` is on: without
   it, the cheapest way to satisfy tag governance would be to delete the key.
3. **A new tag is added in the same change as the concept that needs it.**
   Registry entry and concept in one diff. The concept is the argument for the
   tag, and a reviewer who cannot see the concept cannot judge the word.
4. **Anyone may propose one; nobody adds one alone.** There is no tag owner
   and no request queue — the gate is review, not permission, and it is the
   same review the concept already goes through.
5. **Adding to the registry means adding here too.** A vocabulary nobody can
   read is a spell-checker. If a new tag does not fit one of the facets below,
   that is the signal to talk about the facets rather than to append the tag
   and move on.

# The facets

Tags are grouped by the question they answer. The grouping lives in the
registry's own comments as well, so a reviewer sees where a proposed tag would
sit — and sees when it sits nowhere.

| Facet | Answers | Tags |
| --- | --- | --- |
| Scope | Which project is this about? | `okf`, `okf-net` |
| Domain | Which subject area? | `architecture`, `format`, `practices`, `references` |
| Kind | What is the concept doing? | `domain`, `dogfood`, `meta` |
| The format | Which guarantee of OKF? | `acceptance`, `acknowledgment`, `bundle`, `compatibility`, `compliance`, `conformance`, `conventions`, `interop`, `layout`, `lifecycle`, `readme-trap`, `spec`, `trust`, `verification` |
| Provenance | Which part of the capture path? | `capture`, `custodian`, `maintenance`, `provenance`, `skills`, `sources` |
| Machinery | Which part of the toolset? | `bm25`, `cli`, `configuration`, `determinism`, `diagnostics`, `discovery`, `drift`, `generation`, `index`, `layering`, `lint`, `mcp`, `registry`, `search`, `severity`, `vault` |
| Governance | Which rule about the knowledge itself? | `governance`, `tags` |
| Process | How is the project run? | `agents`, `ci`, `conventional-commits`, `dependencies`, `licensing`, `process`, `release`, `semantic-release`, `versioning` |

**Scope is not optional.** Every concept in this bundle carries `okf` or
`okf-net`, because the single most useful filter over a bundle that documents
both a format and its implementation is *which of the two is this about* —
`okf` marks knowledge that outlives this toolset.

# What the registry is, on day one

The registry was **derived from the tags the bundle had already grown**, not
designed ahead of them. The audit that produced it, taken over the bundle as it
stood at activation:

| | |
| --- | --- |
| Concepts | 19 |
| Distinct tags | 54 |
| Tags used exactly once | 41 |
| Most-used tag | `okf-net`, on 13 |

Forty-one words that group nothing are not yet a vocabulary. Several clusters
are visibly one idea wearing three labels — `conformance` / `compatibility` /
`interop` / `acceptance` all sit on the same question, as do `conventions` and
`layout`, and `generation` / `determinism` / `drift`. Consolidating them is
worth doing and is deliberately **not** done here: merging tags rewrites the
frontmatter of concepts across every directory, and that is a change to review
on its own evidence rather than a rider on the change that first made the
vocabulary visible.

So the registry's day-one job is narrower than it sounds, and worth stating
plainly: it does not make the vocabulary good, it stops it getting worse. A new
tag can no longer appear by accident, because the moment one does, CI says so.

# Why warning and not error

`OKF0305` and `OKF0304` are both set to **warning** in `okf/okf.json`, above
their hidden defaults and below the errors this vault promotes.[^decisions]

An error would mean the first author to reach for an obviously right new word
gets a red pipeline instead of a review comment — over a registry that is one
day old and, by the numbers above, has not earned that authority. A warning
still surfaces every unregistered tag in CI output, which is the whole
governance benefit, while leaving the cost of being wrong at the level of a
conversation.

The promotion to error is a decision to take **with evidence**: once the
registry has survived a consolidation pass and a few real additions, the
warning will have stopped teaching anything, and that is the moment it should
block. Recording the trigger is the point — a severity nobody ever revisits is
a severity chosen by inertia.

# Choosing between a tag, a directory, and a type

When something feels like it wants a new tag, one of three things is usually
true:

- **It is the subject.** Then it belongs in the directory tree, and the tree is
  what a reader navigates. A tag that duplicates its own directory name adds
  nothing to a search.
- **It is the kind of document.** Then it is `type`, not a tag. The format
  registers no `type` values centrally — producers pick them, and consumers
  must tolerate ones they have never seen.[^okf-spec] This bundle uses four,
  `Concept`, `Guide`, `Playbook` and `Reference`, and that list is short on
  purpose: a fifth is a decision about the bundle, taken the same way a new
  tag is.
- **It cuts across both.** Then it is a tag, and it should already join at
  least two concepts — a tag on exactly one concept is a note to self, and
  forty-one of them are the state this bundle is climbing out of.

[^okf-spec]: Open Knowledge Format v0.2, §4.1.
[^decisions]: okf-net — Architecture Decisions, §7 and the Q5 resolution.
