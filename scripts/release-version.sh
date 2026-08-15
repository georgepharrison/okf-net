#!/bin/sh
# Derive, from the working tree, the version a local publish should stamp into the binary.
#
# Prints one line: <version>|<revision>|<describe>
#   version    a bare semver, ready for `-p:Version`   (never carries a leading `v`)
#   revision   a short commit for `-p:SourceRevisionId` (may be empty; may end `.dirty`)
#   describe   the raw `git describe` output, or `none`, for the log line
#
# WHY THIS IS A SCRIPT AND NOT MSBUILD
# ------------------------------------
# Shelling out to git from a build would make builds non-deterministic and would fail in a
# source archive with no `.git`. Directory.Build.props documents the SDK-side contract and
# does no arithmetic; deriving a version from the working tree is the caller's job, and
# this is that caller. The `publish` CI job in .gitlab-ci.yml is the other one, and it does
# not use this script at all: there the version is the tag being released, which is
# authoritative and needs no guessing.
#
# WHY THE DISTANCE/SHA TRAILER IS DROPPED
# ---------------------------------------
# `git describe --tags --always --dirty` produces three shapes, and only the first is a
# valid version:
#   v1.0.0-rc.14              exact tag       ->  1.0.0-rc.14
#   v1.0.0-rc.14-3-g1f334c9   3 commits past  ->  1.0.0-rc.14  (+ sha metadata)
#   1f334c9                   no tags at all  ->  0.0.0-dev    (+ sha metadata)
# The trailer is dropped rather than carried into the version; the commit it named travels
# separately as build metadata, from `git rev-parse --short HEAD`, which is where semver
# §10 puts a commit. (The distance itself is not preserved — the sha is what identifies the
# build.) Left in the version, the trailer would be legal semver that sorts ABOVE the tag
# it followed — `1.0.0-rc.14-3-g1f334c9` > `1.0.0-rc.14`, because an alphanumeric
# prerelease identifier outranks a numeric one. That inversion is the trap being avoided.
#
# Fields are `|`-separated rather than newline-separated so one command substitution gets
# all three without word-splitting on an empty revision.

set -eu

describe="$(git describe --tags --always --dirty 2>/dev/null || true)"
revision="$(git rev-parse --short HEAD 2>/dev/null || true)"
case "$describe" in
  *-dirty) revision="${revision:-unknown}.dirty" ;;
esac

case "$describe" in
  v[0-9]*)
    version="${describe#v}"
    version="$(printf '%s' "$version" | sed -E 's/(-[0-9]+-g[0-9a-f]+)?(-dirty)?$//')"
    ;;
  *)
    version="0.0.0-dev"
    ;;
esac

printf '%s|%s|%s\n' "$version" "$revision" "${describe:-none}"
