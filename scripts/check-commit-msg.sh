#!/usr/bin/env bash
# Validate a commit message against Conventional Commits v1.0.0
# (https://www.conventionalcommits.org/en/v1.0.0/).
#
# Shared by .githooks/commit-msg (the git commit-msg hook) and the
# `check-commit` mise task, so both enforce identical rules from one place.
#
# Usage:
#   check-commit-msg.sh                # validate HEAD's commit message
#   check-commit-msg.sh <file>          # validate the first line of a file
#   check-commit-msg.sh -m <message>    # validate a literal message string
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage:
  check-commit-msg.sh              # validate HEAD's commit message
  check-commit-msg.sh <file>       # validate the first line of a file
  check-commit-msg.sh -m <message> # validate a literal message string
EOF
  exit 2
}

if [[ $# -eq 0 ]]; then
  first_line="$(git log -1 --pretty=%B | head -n 1)"
elif [[ "$1" == "-m" ]]; then
  [[ $# -eq 2 ]] || usage
  first_line="$(head -n 1 <<<"$2")"
elif [[ $# -eq 1 ]]; then
  first_line="$(head -n 1 "$1")"
else
  usage
fi

# Skip merge commits.
if [[ "$first_line" == "Merge "* ]]; then
  exit 0
fi

# Skip fixup!/squash! commits (git --fixup / --squash).
if [[ "$first_line" == "fixup!"* || "$first_line" == "squash!"* ]]; then
  exit 0
fi

types="feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert"
pattern="^(${types})(\([^)]+\))?!?: .+"

if [[ ! "$first_line" =~ $pattern ]]; then
  cat >&2 <<EOF
Error: commit message does not follow Conventional Commits v1.0.0.

  <type>[optional scope][!]: <description>

Allowed types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert

Example:
  feat(cli): add okf lint command

  feat!: drop support for legacy frontmatter

Your first line was:
  ${first_line}

See: https://www.conventionalcommits.org/en/v1.0.0/
EOF
  exit 1
fi

exit 0
