---
type: Playbook
title: GitHub Public Releases and Pages
description: GitLab remains the release builder while the GitHub mirror hosts exact releases and a Pages site with only public installers and manifests.
tags: [okf-net, release, distribution, ci, github, pages]
generated: { by: openai-codex/gpt-5.6-luna, at: 2026-08-18T00:00:00Z }
sources:
  - id: issue-66
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/issues/66
    title: Publish the dogfood site and installable releases on the GitHub mirror
    author: "human:ringo"
    last_modified: 2026-08-17
  - id: architecture
    resource: https://gitlab.tychostation.dev/ringo/okf-net/-/blob/dev/docs/architecture.md
    title: okf-net Architecture Spine
    author: "human:ringo"
    last_modified: 2026-08-17
---

GitLab is still canonical for source, tags, builds, and the eight release bytes.[^issue-66]
The public GitHub repository is a downstream push mirror. A tag pipeline publishes the
same verified binaries, archives, manifest, and installers to a matching GitHub Release;
it waits for the mirrored tag to resolve to `CI_COMMIT_SHA`, never creates a missing tag,
uses a draft while uploading, refuses differing existing bytes, and dispatches a Pages
refresh after publication.[^issue-66]

GitHub Actions has one job: render this dogfood site from mirrored `main`, then stage the
three small public files from published releases — `latest.json`, `install.sh`, and
`install.ps1`. Stable, dev, and `v<version>/` directories contain only those files.
Binaries and archives remain release assets, not Pages files.[^issue-66]

The public default is:

```text
https://georgepharrison.github.io/okf-net
```

A manifest keeps its existing relative `path` and authenticated GitLab `url`, and may
add a `downloadUrl` for a validated absolute HTTPS GitHub asset. Consumers with no
`OKF_INSTALL_URL` use that public URL. Consumers that explicitly set `OKF_INSTALL_URL`
use the contained relative `path`, preserving mirrors, fixtures, and the internal host.
Digest verification, HTTPS redirect restrictions, bounded responses, same-filesystem
staging, atomic replacement, channel selection, and pinned versions remain unchanged.

Issue #40 stays open. GitHub is the public downstream home for this interim architecture;
deciding whether it becomes the canonical home is a separate future decision.[^architecture]

[^issue-66]: GitLab work item #66, “Publish the dogfood site and installable releases on the GitHub mirror.”
[^architecture]: okf-net — Architecture Spine, AD-55.
