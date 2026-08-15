---
type: Reference
title: OKF v0.2, the Format
description: A directory of markdown files with YAML frontmatter, whose only hard requirement is a non-empty type.
resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md
tags: [okf, format, spec, conformance, reference]
generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
sources:
  - id: okf-spec
    resource: https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md
    title: Open Knowledge Format (OKF), version 0.2
    author: "team:google-knowledge-catalog"
    last_modified: 2026-07-24
  - id: prd
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/345c5243b76703aac6244b66e6ebf6f273e77da2/docs/prd.md
    title: okf-net — Product Requirements
    author: "human:ringo"
    last_modified: 2026-08-14
---

The Open Knowledge Format is a directory of UTF-8 markdown files, each with a
YAML frontmatter block delimited by `---`.[^okf-spec] There is no schema
registry, no central authority, and no required tooling: if you can `cat` a
file you can read OKF, and if you can `git clone` a repository you can ship
it. That property is the whole design, and it is what makes the format —
not any implementation of it — the interop layer.

# The shape

- A **bundle** is a directory tree of concepts.
- A **concept** is one markdown file: frontmatter plus body.
- **Frontmatter** is the small machine-queryable layer. **Type** is the only
  always-required key; `title`, `description`, `resource`, and `tags` are
  recommended, and the provenance, trust, and lifecycle families are
  optional.
- The **body** is ordinary markdown for humans and language models, with
  structural markdown preferred over freeform prose.
- Directory structure follows the **domain**. Document kind lives in `type`,
  and cross-cutting categorisation lives in `tags` — never in the folder
  names.

`index.md` and `log.md` are the only reserved filenames. Both are optional:
an index is a navigational view of a directory, and a log records notable
updates newest-first. A consumer that finds no index may synthesise one.

# What conformance actually requires

Conformance is deliberately small. A bundle is conformant when:

1. every non-reserved `.md` file has a frontmatter block that parses, and
2. every one of those has a non-empty `type`, and
3. any `index.md` or `log.md` present follows its reserved structure.

Nothing else makes a bundle non-conformant — not a missing `description`,
not an unknown `type` value, not unknown keys, not broken cross-links, not a
missing `index.md`. Non-markdown files in the tree are simply ignored.

This is the line okf-net's linter defaults to and no further: the three rules
above are its only errors out of the box, and everything else it can say is
consumer configuration. See [the lint severity
model](../toolset/lint-severity-model.md) for how that line is drawn in code,
and [foreign-bundle tolerance](foreign-bundle-tolerance.md) for why it
matters that the line is not drawn anywhere else.

# Producer extensions

Producers may add any keys they like; consumers must not reject documents
carrying fields they do not recognise, and should preserve unknown keys when
round-tripping. Google's own `acme_retail` bundle carries a `not:` key for
anti-definitions, which no part of the specification mentions.

The obligation runs deeper than tolerance. okf-net treats a round trip as
lossless down to key insertion order and scalar form — `okf_version: "0.2"`
comes back quoted, a date comes back a date — because a tool that quietly
alphabetises another producer's frontmatter has corrupted the file in a way
no diff reviewer will thank it for.[^prd]

[^okf-spec]: Open Knowledge Format (OKF), version 0.2, §§1–4, 8, 9, 11.
[^prd]: okf-net — Product Requirements, CORE-2.
