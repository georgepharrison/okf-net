# Directory Update Log

## 2026-08-15

* **Update**: `okf init` shipped, and this vault became the thing it reproduces — a test scaffolds a fresh vault and compares its skeleton against this one. The comparison found `okf/raw/.gitignore` missing here, and it was added. Recorded in [bundle self-description conventions](format/bundle-self-description.md) and [vaults, registry, and config](toolset/vault-registry-and-config.md).
* **Update**: `OKF0310` (raw-item-mutated) joined the rule set as okf-net's first vault-scoped diagnostic, promoted to error in `okf/okf.json`. Catalogued in [the lint severity model](toolset/lint-severity-model.md), and [the custodian model](practices/custodian-model.md) no longer claims the capture manifest is invisible to `okf lint` — it is the one part of the record the linter now reads.
* **Update**: `okf inbox` and `okf verify` shipped, and the two concepts describing them now describe what is built rather than what was designed — [trust tiers and the acknowledgment model](format/trust-tiers-and-acknowledgment.md) gains what each command does and where they are deliberately asymmetric, and [the custodian model](practices/custodian-model.md) gains the staleness-refresh loop and the scheduled `custodian-inbox` job. Every concept in this bundle is on the inbox as unacknowledged, which is the honest reading of a vault no second actor has been through.

## 2026-08-14

* **Update**: Tag governance went live — `lint.tagRegistry` in `okf/okf.json` closed the vocabulary at 56 tags, `OKF0304` and `OKF0305` moved to warning, and [tagging discipline](practices/tagging-discipline.md) became the registry's human face.
* **Activation**: The custodian was instantiated on this vault — `okf/custodian/` naming the two skills, the capture manifest's invariants checked in CI and in the pre-commit hook. Recorded in [the custodian model](practices/custodian-model.md).
* **Update**: Ingested [Equipping agents for the real world with Agent Skills](references/agent-skills.md) from the vault's first `raw/` capture, and cited it from [the custodian model](practices/custodian-model.md).
* **Initialization**: Created the bundle — 14 concepts across `format/`, `toolset/`, and `practices/`, written by hand in a single pass and left unverified.
