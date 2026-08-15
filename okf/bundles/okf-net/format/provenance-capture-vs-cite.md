---
type: Playbook
title: Provenance — Capture Versus Cite
description: The one-question test that decides whether a source is cited in place or captured into the vault first.
tags: [okf, provenance, sources, capture, playbook]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-14
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
---

Every source a concept derives from is recorded one of two ways, and one
question decides which:[^decisions]

> **If this source changed or vanished tomorrow, could the custodian still
> re-verify the concept?**

# Yes — cite it

Repository code, a database you own, version-pinned library documentation, a
git tag: all of these can be re-read on demand, and the answer is to point at
them. Add a `sources` entry with `resource`, a `last_modified` date, and a
version pin wherever one exists.

Citing is the spec-native path.[^okf-spec] A `sources[].resource` may be an
absolute URL, a bundle-relative path, a relative path, or — usefully — a
*scope descriptor* that no consumer can dereference at all, such as
`all queries in BigQuery project X`. The credibility signals `author`,
`usage_count`, and `last_modified` hang off the same entry.

# No — capture it, then cite the capture

A blog post, a conference talk, a PDF, a page that will 404 in eighteen
months: these cannot defend themselves. Drop the artifact into `raw/`, the
vault's drop zone, then *ingest* it into an ordinary conformant concept under
the bundle's `references/`, and cite that concept. The original URL is kept
as a courtesy field, not as the evidence.

Two details that are easy to get wrong:

- `raw/` lives **outside** every bundle root. A raw `.md` file inside a
  bundle would be a frontmatter-less concept and would break conformance the
  moment it landed. See [bundle self-description](bundle-self-description.md).
- `references/` carries **no** okf-net-specific meaning. It is the plain
  specification convention — a directory that mirrors external material as
  first-class concepts — so that a foreign bundle using it is never
  reinterpreted. Immutability is tracked on `raw/` items through a capture
  manifest, not inferred from a directory name.

Captured material is flat by default. Only lossy formats — PDF, video — are
captured as a packet: the original artifact alongside an `extracted.md`.

# Attribution is keyed, never positional

A claim attributable to a specific source is footnoted with a label equal to
that source's `id`:

```markdown
The `events_` table is sharded daily.[^ga4-schema]

[^ga4-schema]: GA4 BigQuery Export schema
```

The label is the join key into `sources`. Positional references break
silently the first time an agent reorders the list, and agents rewrite these
documents constantly. okf-net lints the join in both directions: a footnote
with no matching `id`, and an `id` nothing ever cites.

# The compensating control

Citing a live source trades durability for freshness, so the trade needs a
tripwire. Two exist, and neither blocks a build by default:

- **Source drift** — a `sources[].last_modified` later than the concept's
  `generated.at` means the world moved after the concept was written.
- **Staleness** — `stale_after` has passed, which is the signal that will
  eventually wake the custodian's refresh loop.

Both are reported by `okf lint`; see [trust tiers and
acknowledgment](trust-tiers-and-acknowledgment.md) for what a consumer does
with the answer.

[^decisions]: okf-net — Architecture Decisions, §5 and Q3.
[^okf-spec]: Open Knowledge Format (OKF), version 0.2, §§5.1, 6.2, 6.3.
