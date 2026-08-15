# Agent skills

Three skills for working an OKF knowledge vault, one per job, all three
standing on the same sentence:

> **Orient by disclosure, retrieve by search, open what you pick.** Read
> `index.md` to learn what a bundle holds, `okf search` to find candidates,
> and the concept files search pointed at.

| Skill | Fires when |
| --- | --- |
| [`okf-capture`](okf-capture/SKILL.md) | something just learned is worth keeping — *remember this*, *write this up* — or a link, PDF, or talk needs to outlive its URL |
| [`okf-custodian`](okf-custodian/SKILL.md) | a bundle needs maintaining — captures waiting in `raw/`, index drift, stale concepts, prose to enrich |
| [`okf-vault`](okf-vault/SKILL.md) | a question could be answered from the vault — *what do we know about X* — or the human asks what needs attention |

Getting knowledge in is `okf-capture`'s; keeping it alive is
`okf-custodian`'s; getting it back out is `okf-vault`'s. The two producers
hand off to each other — capture drops the artifact, the custodian ingests it
— and `okf-vault` hands back to both at its write boundary, so a reading
session that turns up something worth keeping arrives at the right skill.
