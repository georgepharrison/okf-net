---
type: Reference
title: The Architecture Spine
description: A lean contract of numbered AD rules — the calls a future builder cannot read off compliant code — with decisions.md kept as the "why" log behind it.
resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/80cd6fde76a4afb62adc2256141d130e5d186723/docs/architecture.md
tags: [okf-net, architecture, layering, governance]
generated: { by: "openai-codex/gpt-5.6-luna", at: 2026-08-18T00:00:00Z }
sources:
  - id: spine
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/80cd6fde76a4afb62adc2256141d130e5d186723/docs/architecture.md
    title: okf-net — Architecture Spine
    author: "human:ringo"
    last_modified: 2026-08-15
  - id: decisions
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/80cd6fde76a4afb62adc2256141d130e5d186723/docs/decisions.md
    title: okf-net — Architecture Decisions (running log)
    author: "human:ringo"
    last_modified: 2026-08-15
---

okf-net keeps three documents about itself, and they answer three different
questions. `decisions.md` is the running **why** — every option weighed, every
measurement taken, every ruling dated. `prd.md` is the requirement-shaped
**what**, numbered and testable. The **architecture spine** is neither: it is
the consistency contract in between, holding only the calls two
independently-built units could otherwise make incompatibly.[^spine]

The spine's `Okf.Core` is an internal library layer behind the executable, not a
distributed NuGet API. It is explicitly non-packable; releases ship executable
binaries and knowledge and skills archives, not a library artifact.

The test for what belongs is a single question. *If two units one level down
were built independently, could they choose incompatibly?* Fix it in the spine
only when the answer is yes, the call is non-obvious, and it is a real
trade-off. Everything else is named under Deferred and left to the code.

# The shape is deterministic; the prose is not

The document's sections are fixed, in this order:

* **Design Paradigm** — the one-paragraph stance a named pattern buys for free.
* **Inherited Invariants** — what OKF v0.2 and .NET/AOT impose and okf-net
  cannot change.
* **Invariants & Rules** — the numbered `AD` decisions, the load-bearing half.
* **Consistency Conventions** — the defaults that bind where builders drift.
* **Stack** — the pinned dependency and tooling list.
* **Structural Seed** — the container view and the key flows, as diagrams.
* **Capability → Architecture Map** — capability to component to `AD` to
  requirement id.
* **Deferred** — what the spine deliberately does not decide, and why it can
  wait.

That skeleton is the point. Prose written by a model is not reproducible; a
section list is. Running the same workflow twice yields the same document
shape filled with the current truth, which is what makes the spine reviewable
against the sources rather than against memory.

# What an AD looks like

Each decision is a stable identifier plus three fields, and every one links
back to the `decisions.md` entry that argued it:

* **Binds** — the capabilities, components or areas it governs.
* **Prevents** — the specific divergence it stops. An `AD` that prevents
  nothing is a description, not a rule.
* **Rule** — the constraint downstream must follow, stated so it can be
  checked.

The fifty-six rules cover layering and the offline contract — including the
single, bounded exception to it, the self-replacing `okf upgrade` — the diagnostic
ranges and the severity model, provenance capture-versus-cite and the capture
manifest, which bookkeeping a verb writes rather than an agent,
trust derivation and acknowledgment, the canonical timestamp,
configuration precedence, search determinism and its engine-agnostic result
contract, MCP containment, the bundler's dangling-and-record answer, the
site's untrusted-data posture, the single bundle walk every surface reads, the
shared concept walk that decides what an inventory of concepts is complete over,
release stamping, the exact-byte GitLab-built GitHub release and Pages boundary,
the license gate, the split between what a commit hook may
cost and what CI owes, the registry as the only way scope widens, and the rule
that tests must be shown to constrain the code.

# The rules the document keeps about itself

Three conventions make the spine safe to regenerate:

1. **`AD` identifiers are append-only.** They ascend, are never renumbered,
   and a retired identifier is never reused. Downstream work cites them, so a
   renumbering would silently re-point every citation.
2. **A superseded `AD` is struck, not deleted.** Its `Binds` / `Prevents` /
   `Rule` stay on the page with a pointer to the decision that replaced it,
   because the record of what was once true is what makes a reversal
   reviewable.
3. **Rationale stays in `decisions.md`.** The spine records the decision; the
   argument, the measurements and the rejected alternatives stay in the log
   that already holds them.[^decisions]

# How it relates to the rest of the vault

The public-release rule keeps GitLab as the builder while GitHub is the downstream
public release and Pages host; issue #40 remains the future canonical-home decision.

The spine is a repository document, not a bundle concept, for the same reason
`decisions.md` and `prd.md` are: `docs/` holds frontmatter-less markdown and a
bundle root cannot. This concept is the bundle's pointer at it — the shape,
the vocabulary, and the maintenance rules, so an agent orienting through the
vault learns the document exists and what it is for before opening it.

[^spine]: okf-net — Architecture Spine, header and Invariants & Rules.
[^decisions]: okf-net — Architecture Decisions, the 1.0.0 review.
