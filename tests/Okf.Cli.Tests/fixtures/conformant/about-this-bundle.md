---
type: Guide
title: About this bundle
description: A conformant fixture bundle used by the okf-net test suite.
tags: [meta, fixture]
generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }
verified:
  - { by: human:tests@example.invalid, at: 2026-01-02T00:00:00Z }
status: stable
sources:
  - id: okf-spec
    resource: https://example.invalid/okf/spec
    title: OKF v0.2
    last_modified: 2025-12-01
---

# About this bundle

This bundle exists so that `okf lint` has something clean to walk: it exercises a
bundle-root `index.md` carrying `okf_version`, a `log.md`, a subdirectory with its own
index, footnote-based attribution, and both link forms.[^okf-spec]

See the [widgets concept](/topics/widgets.md) and this bundle's [log](log.md).

[^okf-spec]: OKF v0.2
