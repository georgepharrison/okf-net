---
type: Reference
title: Equipping Agents for the Real World with Agent Skills
description: Anthropic's description of Agent Skills — a directory holding a SKILL.md whose name and description are the first level of progressive disclosure.
resource: https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills
tags: [okf-net, skills, agents, capture]
generated: { by: claude-fable/5, at: 2026-08-14T23:54:00-05:00 }
sources:
  - id: agent-skills
    resource: https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills
    title: Equipping agents for the real world with Agent Skills
    author: "Barry Zhang, Keith Lazuka, Mahesh Murag (Anthropic)"
    last_modified: 2025-12-18
---

Captured 2026-08-14 as raw item `2026-08-14-equipping-agents-with-agent-skills`
(`okf/raw/manifest.json`). Published 16 October 2025 and updated 18 December
2025 to record that the format had been released as an open standard for
cross-platform portability.[^agent-skills]

This is a rendering of the source, not an argument about it. What it means for
okf-net's own skills is argued in [the custodian
model](../practices/custodian-model.md).

# What a skill is

A skill is a **directory containing a `SKILL.md` file**, alongside whatever
instructions, scripts, and resources the author bundles with it. The stated
analogy is an onboarding guide for a new hire: procedural knowledge and
organizational context, packaged so that a general-purpose agent can be
specialized without being rebuilt.

`SKILL.md` must begin with YAML frontmatter carrying two required keys:

| Key | Role |
| --- | --- |
| `name` | Identifies the skill. |
| `description` | Says when the skill should be used. |

At startup the agent pre-loads the `name` and `description` of every installed
skill into its system prompt. Nothing else about the skill is loaded until it
is needed.

# Progressive disclosure

Progressive disclosure is named as **the core design principle** that makes
skills flexible and scalable, and it is described in three levels:

1. **Metadata.** `name` and `description`, in the system prompt at startup —
   just enough for the agent to know when the skill applies, without loading
   the skill.
2. **The body.** The full `SKILL.md`, read into context when the agent judges
   the skill relevant to the task at hand.
3. **Bundled files.** Additional files in the skill directory, referenced by
   name from `SKILL.md` and read only when the situation calls for them.

The worked example is a PDF skill whose form-filling instructions live in a
separate `forms.md`, so the core of the skill stays lean and `forms.md` is read
only when a form is actually being filled.

The consequence the article draws from this is the one that matters: an agent
with a filesystem and code execution does not need to read a skill in its
entirety, so *the amount of context that can be bundled into a skill is
effectively unbounded*.[^agent-skills]

# Skills and code execution

Skills may bundle code for the agent to execute at its discretion. The argument
for it is cost and determinism together: sorting a list by token generation is
far more expensive than running a sorting algorithm, and many applications need
a reliability that only code provides. In the PDF example, a bundled Python
script extracts a document's form fields, and the agent runs it without reading
either the script or the PDF into context.

# Guidance for authors

Four practices are offered:

- **Start with evaluation.** Find the gaps by running agents on representative
  tasks and watching where they struggle, then build skills incrementally
  against what was observed.
- **Structure for scale.** Split an unwieldy `SKILL.md` into referenced files;
  keep mutually exclusive paths separate to reduce token usage; make it clear
  whether code is to be executed or read.
- **Think from the agent's perspective.** Watch how the skill is actually used
  and iterate, paying particular attention to `name` and `description`, since
  those are what the decision to trigger the skill is made from.
- **Iterate with the agent.** Have it capture its successful approaches and
  common mistakes into the skill, and self-reflect when a run goes off track,
  rather than trying to anticipate the needed context up front.

# Security

Skills grant new capabilities through instructions and code, so a malicious
skill can introduce vulnerabilities or direct an agent to exfiltrate data. The
recommendation is to install skills only from trusted sources, and to audit an
unfamiliar one before use — reading the bundled files, with particular
attention to code dependencies, bundled resources, and any instruction to reach
an untrusted external network source.

# Where it was going

At publication, skills were supported across Claude.ai, Claude Code, the Claude
Agent SDK, and the Claude Developer Platform. The stated direction was to fill
out the lifecycle of creating, editing, discovering, sharing, and using skills;
to explore how skills complement MCP servers by teaching agents workflows that
involve external tools; and, further out, to let agents create, edit, and
evaluate skills themselves.

[^agent-skills]: Equipping agents for the real world with Agent Skills, "The anatomy of a skill" and "Skills and code execution".
