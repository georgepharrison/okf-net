# okf-net's own OKF vault

This directory is okf-net's **dogfood vault**: the toolset's own knowledge,
kept as an OKF v0.2 bundle and validated by the toolset itself on every
pipeline run (PRD ACC-5).

It is also the project's public worked example of *documenting a code repo
with OKF*. The pattern is generic; the fact that the repo happens to hold
C# is incidental flavour.

## Layout

```text
okf/
├─ README.md          # this file — repo-facing, deliberately OUTSIDE every bundle root
├─ okf.json           # project config: the team contract for lint severities and tags
├─ custodian/         # the machinery that maintains the bundle (stripped by the bundler)
└─ bundles/
   └─ okf-net/        # the bundle root
```

`README.md` sits here rather than inside `bundles/okf-net/` on purpose. OKF
does not reserve the name `README.md`, so a README inside a bundle root
would be read as a frontmatter-less concept and would fail §11 conformance.
The rule that falls out of this — **bundle root is never repo root** — is
recorded as a concept in the bundle itself, under
[`bundles/okf-net/format/bundle-self-description.md`](bundles/okf-net/format/bundle-self-description.md).

`custodian/` holds the machinery that maintains the bundle — the recipe
naming the two skills that do the work, and the capture-manifest check CI
runs beside `okf lint`. It is producer-side and the bundler strips it; see
[`custodian/README.md`](custodian/README.md) for what runs where, and the
bundle's `about-this-bundle.md` for the current custodial status.

## Working with it

Every command below is run from the repo root. `mise run cli --` is a local
alias for `dotnet run --project src/Okf.Cli --`.

| Task | Command |
| --- | --- |
| Lint the vault | `mise run cli -- lint okf/` |
| Regenerate indexes | `mise run cli -- index okf/bundles/okf-net` |
| Fail on index drift | `mise run cli -- index --check okf/bundles/okf-net` |
| Search the bundle | `mise run cli -- search "custodian" okf/` |
| Check the capture manifest | `python3 okf/custodian/check-manifest.py` |

The `dogfood` job in `.gitlab-ci.yml` runs the lint, the drift check, and
the manifest check from the same pipeline's source, so breaking the tool
breaks this build.

## What belongs here

Curated knowledge **about** the toolset: the format it implements, the
architecture, the conventions, the policies. `docs/decisions.md` and
`docs/prd.md` remain the authoritative development documents — the bundle
cites them, it does not replace them, and it never grows into a second
place to look for a decision's rationale.

This repo never holds anyone's personal knowledge vault. That lives at
`~/okf/`, reached through the registry, and never here.
