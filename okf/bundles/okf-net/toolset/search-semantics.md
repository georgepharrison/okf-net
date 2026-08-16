---
type: Reference
title: Search Semantics
description: Deterministic BM25 over field-weighted concept text, links-first results, and filters that never contribute score.
tags: [okf-net, search, bm25, determinism]
generated: { by: claude-fable/5, at: 2026-08-16T09:00:00Z }
sources:
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

`okf search` is **deterministic BM25-style lexical search**: tokenised
matching, frontmatter weighted above body text, query terms AND-ed with an OR
fallback, ties broken by path.[^decisions] No clock, no network, no model
call — the same tree and the same query produce the same ranking on every
machine and in CI, which is the only reason a hook or a pipeline can depend
on the answer.

Substring matching was considered and rejected: it gives no ranking, and a
query for `metric` hitting `metrics` but not `Metric` forces case folding
anyway, at which point tokens are simply the simpler design. Shipping the
vectorisation spike early was rejected too — it is out of MVP scope, it needs
a generated artifact the bundle must not depend on, and it is not
deterministic in the way CI needs.

# The corpus

**Concept documents only.**

- `raw/` is never searched. It sits outside every bundle root by
  construction, so bundle-scoped discovery cannot reach it — and this is
  asserted by test rather than assumed.
- The reserved files `index.md` and `log.md` are never corpus entries. An
  index is a *view* of the concepts, so indexing it would double-count every
  title and description and rank navigation above content.
- A concept whose frontmatter does not parse is not a corpus entry either. It
  is already an `OKF0001` error, and `okf lint` is the surface that says so.

# Scoring

BM25 with the standard `k1 = 1.2`, `b = 0.75`, over field-weighted term
frequencies:

| Field | Weight |
| --- | --- |
| `title` | ×3 |
| `tags` | ×2 |
| `type` | ×2 |
| `description` | ×2 |
| body | ×1 |

`type` is weighted alongside `tags` because it is the same kind of
categorical metadata and the requirement to make it matchable has to land
somewhere. IDF is the non-negative variant, so a term present in most of the
corpus can never *subtract* from a score.

Collection statistics — document count, document frequency, average length —
are computed over the **whole resolved corpus, not the filtered candidate
set**. A filter says which concepts may be returned; it does not change what
the collection is, so adding a filter never reshuffles the results that
survive it.

Tokenisation is lowercase, Unicode-aware, and splits on anything that is not
a letter or digit, in the query and in the corpus alike — so `sqlite-vec` is
the two terms `sqlite` and `vec` and matches consistently either way. There
is deliberately **no stemming, no stopword list, and no minimum token
length** in v1: a stemmer is per-language state a `cat`-readable format
should not need, and it would be the first thing any other implementation of
the format's tooling had to reproduce bit-for-bit.

# Filters

`tag:<x>` and `type:<y>`, written inline in the query or spelled as
`--tag`/`--type` flags — the same filters either way. Comparison is
case-insensitive on the whole value, not on tokens, so `tag:cost-optimization`
never matches a concept tagged `cost`.

Repetition folds by the arity of the field: repeated `type:` is **OR**,
because a concept has exactly one `type` and AND-ing two would always return
nothing; repeated `tag:` is **AND**, because a concept has many tags and
narrowing is what a second filter is for. Filters restrict candidates
*before* scoring and never contribute to score.

# Results

Links-first, always: path, title, type, score, trust tier, stale flag, and a
bounded snippet — **never a full body**. Search points *into* progressive
disclosure rather than replacing it, and reading a concept is a different
operation. The trust and staleness fields are there so an agent can judge a
hit before spending a read on it.

Snippets are a single ~160-character window of the body chosen to cover the
most distinct matched terms, trimmed to word boundaries, with matched terms
marked `**like this**` — markdown emphasis, because the payload is markdown
and the same string has to read well in a terminal and in an MCP client. A
frontmatter-only match snippets the `description` instead.

The JSON record is **engine-agnostic**: nothing in it names BM25, tokens, or
fields. `score` is an opaque, higher-is-better number and `matchMode` says
only `all`, `any`, or `filter`. The vectorisation spike can be slotted behind
the identical shape later without breaking a single MCP consumer.[^prd]

# Two decisions worth remembering

- **Search defaults to project scope.** The same vault resolution `okf lint`
  performs and nothing wider; registry entries, including the personal vault,
  stay opt-in, and the opt-in is `--scope personal|registered|all` or
  `search.scope` in `okf.json`. See [vaults, registry, and
  config](vault-registry-and-config.md).
- **No results is exit code 0, not 1.** The grep convention was considered
  and rejected: an empty result set is not a diagnostic, and the capture
  skill's search-before-create loop would otherwise fail a commit in the
  normal case. Exit 2 keeps its usual meaning of usage failure, including an
  empty query with no filters.

[^decisions]: okf-net — Architecture Decisions, Q7 resolution and "Scoring, as shipped".
[^prd]: okf-net — Product Requirements, CORE-11, CLI-11, MCP-2.
