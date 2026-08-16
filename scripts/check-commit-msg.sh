#!/usr/bin/env bash
# Validate a commit message against Conventional Commits v1.0.0
# (https://www.conventionalcommits.org/en/v1.0.0/).
#
# Shared by .githooks/commit-msg (the git commit-msg hook) and the
# `check-commit` mise task, so both enforce identical rules from one place.
#
# Also enforces Ringo's 2026-08-16 rule (docs/decisions.md, "Proposed
# decisions: no major bumps"): okf-net stays on 1.x, so no commit may carry a
# Conventional Commits breaking-change marker — a `!` after the type/scope in
# the header, or a `BREAKING CHANGE:`/`BREAKING-CHANGE:` footer — because
# either form makes semantic-release's commit-analyzer (.releaserc.yml) cut a
# major (2.0.0) release.
#
# Usage:
#   check-commit-msg.sh                # validate HEAD's commit message
#   check-commit-msg.sh <file>          # validate the whole file
#   check-commit-msg.sh -m <message>    # validate a literal message string
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage:
  check-commit-msg.sh              # validate HEAD's commit message
  check-commit-msg.sh <file>       # validate the whole file
  check-commit-msg.sh -m <message> # validate a literal message string
EOF
  exit 2
}

# The full message body, in all three modes — the breaking-change footer
# check below needs to read past the first line, even though the type/scope
# check that follows only ever looks at line 1.
if [[ $# -eq 0 ]]; then
  message="$(git log -1 --pretty=%B)"
elif [[ "$1" == "-m" ]]; then
  [[ $# -eq 2 ]] || usage
  message="$2"
elif [[ $# -eq 1 ]]; then
  message="$(cat "$1")"
else
  usage
fi

first_line="$(head -n 1 <<<"$message")"

# Skip merge commits.
if [[ "$first_line" == "Merge "* ]]; then
  exit 0
fi

# Skip fixup!/squash! commits (git --fixup / --squash).
if [[ "$first_line" == "fixup!"* || "$first_line" == "squash!"* ]]; then
  exit 0
fi

types="feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert"

# Ringo's 1.x rule: a `!` right after the type/scope in the header.
breaking_header_pattern="^(${types})(\([^)]+\))?!: "
if [[ "$first_line" =~ $breaking_header_pattern ]]; then
  cat >&2 <<EOF
Error: commit header carries a breaking-change marker ("!").

okf-net stays on 1.x (Ringo's rule, 2026-08-16 — see docs/decisions.md,
"Proposed decisions: no major bumps"): a "!" after the type/scope would make
semantic-release's commit-analyzer (.releaserc.yml) cut a major (2.0.0)
release. Drop the "!"; describe an incompatible change in prose instead of
marking it as one.

Your first line was:
  ${first_line}

See: https://www.conventionalcommits.org/en/v1.0.0/
EOF
  exit 1
fi

# Ringo's 1.x rule: a BREAKING CHANGE / BREAKING-CHANGE: footer, anywhere in
# the body. Matched at line start only, so prose that merely mentions
# "breaking change" (lowercase, mid-sentence) is unaffected — only the
# Conventional Commits footer token is refused.
if grep -qE '^BREAKING[ -]CHANGE:' <<<"$message"; then
  cat >&2 <<EOF
Error: commit message has a BREAKING CHANGE / BREAKING-CHANGE: footer.

okf-net stays on 1.x (Ringo's rule, 2026-08-16 — see docs/decisions.md,
"Proposed decisions: no major bumps"): that footer would make
semantic-release's commit-analyzer (.releaserc.yml) cut a major (2.0.0)
release. Remove the footer; describe the change in the body instead.

See: https://www.conventionalcommits.org/en/v1.0.0/
EOF
  exit 1
fi

pattern="^(${types})(\([^)]+\))?: .+"

if [[ ! "$first_line" =~ $pattern ]]; then
  cat >&2 <<EOF
Error: commit message does not follow Conventional Commits v1.0.0.

  <type>[optional scope]: <description>

Allowed types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert

Example:
  feat(cli): add okf lint command

Your first line was:
  ${first_line}

See: https://www.conventionalcommits.org/en/v1.0.0/
EOF
  exit 1
fi

exit 0
