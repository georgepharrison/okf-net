#!/usr/bin/env bash
# Tests for scripts/check-commit-msg.sh: Conventional Commits shape, plus
# Ringo's 2026-08-16 rule that okf-net stays on 1.x (no `!` header marker, no
# BREAKING CHANGE:/BREAKING-CHANGE: footer). See docs/decisions.md, "Proposed
# decisions: no major bumps (2026-08-16)".
#
# Run it with `mise run check-commit-test`, or directly:
#   scripts/check-commit-msg.test.sh
#
# Every FAIL-shaped case below was shown to pass against the pre-guard
# script (it only ever checked `<type>(<scope>)?!?: .+` against line 1, so
# `feat!:` and `feat(core)!:` were accepted, and BREAKING CHANGE/-CHANGE:
# footers were never looked at — all three input modes fed it only the first
# line). See AD-44 (docs/decisions.md) for the record.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/check-commit-msg.sh"

[[ -x "$script" ]] || { echo "no executable check-commit-msg.sh at $script" >&2; exit 2; }

pass=0
fail=0

ok()  { printf '  \033[32mok\033[0m   %s\n' "$1"; pass=$((pass + 1)); }
bad() { printf '  \033[31mFAIL\033[0m %s\n' "$1"; printf '       %s\n' "$2"; fail=$((fail + 1)); }

# assert_pass <label> <message>
assert_pass() {
  local label="$1" message="$2" out rc
  set +e
  out="$("$script" -m "$message" 2>&1)"
  rc=$?
  set -e
  if [[ "$rc" -eq 0 ]]; then
    ok "$label"
  else
    bad "$label" "expected exit 0, got $rc:${NL}$out"
  fi
}

# assert_fail <label> <message> <needle-in-stderr>
assert_fail() {
  local label="$1" message="$2" needle="$3" out rc
  set +e
  out="$("$script" -m "$message" 2>&1)"
  rc=$?
  set -e
  if [[ "$rc" -eq 0 ]]; then
    bad "$label" "expected a non-zero exit, got 0"
  elif [[ "$out" != *"$needle"* ]]; then
    bad "$label" "expected to find [$needle] in:${NL}$out"
  else
    ok "$label"
  fi
}

NL=$'\n'

# ---------------------------------------------------------------------------
# Ordinary Conventional Commits, unaffected by the 1.x guard.
# ---------------------------------------------------------------------------

assert_pass "a plain feat: passes"           "feat: add x"
assert_pass "a plain fix: passes"            "fix: y"
assert_pass "a plain docs: passes"           "docs: z"
assert_pass "a scoped feat passes"           "feat(cli): add okf lint command"

# ---------------------------------------------------------------------------
# The header `!` marker — both unscoped and scoped.
# ---------------------------------------------------------------------------

assert_fail "feat!: is refused"        "feat!: x"        "1.x"
assert_fail "feat(core)!: is refused"  "feat(core)!: x"  "1.x"

# ---------------------------------------------------------------------------
# The footer token — space and hyphen spellings — read from the WHOLE
# message, not just the first line.
# ---------------------------------------------------------------------------

assert_fail "a BREAKING CHANGE: footer is refused" \
  "$(printf 'fix: y\n\nBREAKING CHANGE: the api changed')" "1.x"
assert_fail "a BREAKING-CHANGE: footer is refused" \
  "$(printf 'fix: y\n\nBREAKING-CHANGE: the api changed')" "1.x"

# Prose that merely uses the words, mid-sentence and lowercase, is not the
# Conventional Commits footer token — only `^BREAKING[ -]CHANGE:` at line
# start is refused.
assert_pass "prose mentioning \"breaking change\" (lowercase) passes" \
  "$(printf 'fix: y\n\nThis is not a breaking change to behavior.')"

# ---------------------------------------------------------------------------
# Skips that must still work with the guard in place.
# ---------------------------------------------------------------------------

assert_pass "a merge commit is skipped" "Merge branch 'foo' into 'bar'"
assert_pass "a merge commit is skipped even carrying a footer token" \
  "$(printf "Merge branch 'foo' into 'bar'\n\nBREAKING CHANGE: irrelevant, this line is never read")"
assert_pass "a fixup! commit is skipped" "fixup! feat: add x"
assert_pass "a squash! commit is skipped" "squash! feat: add x"

# ---------------------------------------------------------------------------

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]
