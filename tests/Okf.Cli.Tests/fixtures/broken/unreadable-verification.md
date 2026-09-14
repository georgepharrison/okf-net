---
type: Reference
title: Unreadable verification
description: A concept whose verified block cannot be read as events, so OKF0203 has a shape to fire on.
tags: [fixture]
generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }
verified: ahormati
---

# Unreadable verification

This concept claims verification in a shape §5.2 cannot read: `verified` is a scalar, so the
normalizer finds zero events and the trust tier reads `unverified`. `okf lint` reports the shape
(`OKF0203`) and `okf candidates` quarantines it, because either surface reading this as "no
history" would let a malformed block hide a concept from review.
