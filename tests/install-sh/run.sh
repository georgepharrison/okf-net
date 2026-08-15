#!/usr/bin/env bash
# Acceptance tests for the repository-root install.sh.
#
# `install.sh` is the one thing in this repo a stranger runs before anything else exists on
# their machine, and it is the one thing the .NET suite cannot reach: it is POSIX sh, it
# talks HTTP, and its failure mode is a half-installed binary. So it gets tested the way it
# is used — against a real HTTP server serving a real release tree, with the real installer
# invoked as a subprocess and its exit code and filesystem effects asserted.
#
# The harness is bash (arrays and `trap ERR` earn their keep here); the SCRIPT UNDER TEST is
# run under `sh`, and under `dash` too when dash is installed, because a bashism that only
# bash forgives is exactly the bug this file exists to catch.
#
# Run it with `mise run test-install`, or directly:
#   tests/install-sh/run.sh
#
# Every case below was shown to fail before install.sh implemented the behaviour it
# asserts (AGENTS.md: "tests must be shown to constrain the code"); see the branch's commit
# message for the record of which assertion caught which missing behaviour.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
installer="$repo/install.sh"

[[ -f "$installer" ]] || { echo "no install.sh at $installer" >&2; exit 2; }

command -v python3 >/dev/null || { echo "python3 is required to serve the fixture" >&2; exit 2; }

work="$(mktemp -d "${TMPDIR:-/tmp}/okf-install-tests.XXXXXX")"
server_pid=""
cleanup() {
  [[ -n "$server_pid" ]] && kill "$server_pid" 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

pass=0
fail=0

ok()   { printf '  \033[32mok\033[0m   %s\n' "$1"; pass=$((pass + 1)); }
bad()  { printf '  \033[31mFAIL\033[0m %s\n' "$1"; printf '       %s\n' "$2"; fail=$((fail + 1)); }
note() { printf '\n\033[1m%s\033[0m\n' "$1"; }

check_eq() { # check_eq <label> <expected> <actual>
  if [[ "$2" == "$3" ]]; then ok "$1"; else bad "$1" "expected [$2], got [$3]"; fi
}

check_contains() { # check_contains <label> <needle> <haystack>
  if [[ "$3" == *"$2"* ]]; then ok "$1"; else bad "$1" "expected to find [$2] in:${NL}$3"; fi
}

check_not_contains() { # check_not_contains <label> <needle> <haystack>
  if [[ "$3" != *"$2"* ]]; then ok "$1"; else bad "$1" "did not expect [$2] in:${NL}$3"; fi
}

NL=$'\n'

# ---------------------------------------------------------------------------
# The fixture: a www tree shaped exactly like the one the artifact host serves, with two
# versions in it so that "newest" and "--version" are distinguishable answers.
# ---------------------------------------------------------------------------

www="$work/www"
mkdir -p "$www"

VERSION_NEW="1.0.0-rc.16"
VERSION_OLD="1.0.0-rc.15"

make_release() { # make_release <version>
  local v="$1" dir="$www/v$1" sha_bin sha_bundle
  mkdir -p "$dir"

  # A stand-in for the NativeAOT binary: a script that answers `okf version` the way the
  # real one does, so the installer's final "prints the installed version" step is exercised
  # rather than mocked away.
  cat >"$dir/okf-linux-x64" <<EOF
#!/bin/sh
[ "\${1:-}" = version ] && echo "$v+abc1234" && exit 0
echo "okf $v" && exit 0
EOF
  chmod +x "$dir/okf-linux-x64"

  printf 'not really a tarball, version %s\n' "$v" >"$dir/okf-net-knowledge.tar.gz"
  cp "$installer" "$dir/install.sh"

  sha_bin="$(sha256sum "$dir/okf-linux-x64" | cut -d' ' -f1)"
  sha_bundle="$(sha256sum "$dir/okf-net-knowledge.tar.gz" | cut -d' ' -f1)"

  # Written the way .gitlab-ci.yml's `publish` job writes it — same key order, same
  # indentation, same relative `path`. If that job's layout drifts from this fixture, the
  # sed-based reader in install.sh is the thing that breaks, and this is where it shows.
  cat >"$dir/latest.json" <<EOF
{
  "version": "$v",
  "tag": "v$v",
  "generatedAt": "2026-08-15T08:30:00Z",
  "assets": {
    "okf-linux-x64": {
      "path": "v$v/okf-linux-x64",
      "size": $(wc -c <"$dir/okf-linux-x64"),
      "sha256": "$sha_bin",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-linux-x64"
    },
    "okf-net-knowledge.tar.gz": {
      "path": "v$v/okf-net-knowledge.tar.gz",
      "size": $(wc -c <"$dir/okf-net-knowledge.tar.gz"),
      "sha256": "$sha_bundle",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-net-knowledge.tar.gz"
    }
  }
}
EOF
}

make_release "$VERSION_OLD"
make_release "$VERSION_NEW"

# The root is the "latest" channel: copies of the newest version, exactly as sync.sh
# publishes them on the artifact host.
for f in latest.json install.sh okf-linux-x64 okf-net-knowledge.tar.gz; do
  cp "$www/v$VERSION_NEW/$f" "$www/$f"
done

# ---------------------------------------------------------------------------
# Serve it.
# ---------------------------------------------------------------------------

port=""
for candidate in $(seq 8801 8830); do
  if ! (exec 3<>"/dev/tcp/127.0.0.1/$candidate") 2>/dev/null; then
    port="$candidate"
    break
  fi
done
[[ -n "$port" ]] || { echo "no free port in 8801-8830" >&2; exit 2; }

python3 -m http.server "$port" --bind 127.0.0.1 --directory "$www" >"$work/server.log" 2>&1 &
server_pid=$!

base="http://127.0.0.1:$port"
for _ in $(seq 1 50); do
  if curl -fsS "$base/latest.json" >/dev/null 2>&1; then break; fi
  sleep 0.1
done
curl -fsS "$base/latest.json" >/dev/null || { echo "fixture server never came up" >&2; cat "$work/server.log" >&2; exit 2; }

# ---------------------------------------------------------------------------
# run <shell> <install-dir> [args...] — invoke the installer, capture rc + merged output.
# ---------------------------------------------------------------------------

out=""
rc=0
run() {
  local shell="$1" dir="$2"; shift 2
  set +e
  out="$(OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" "$shell" "$installer" "$@" 2>&1)"
  rc=$?
  set -e
}

shells=(sh)
if command -v dash >/dev/null 2>&1; then
  shells+=(dash)
fi
echo "shells under test: ${shells[*]}"

for sh_bin in "${shells[@]}"; do

note "[$sh_bin] happy path"
dir="$work/bin-happy-$sh_bin"
run "$sh_bin" "$dir"
check_eq   "exits 0" "0" "$rc"
check_contains "reports the resolved version" "$VERSION_NEW" "$out"
check_contains "verifies the digest" "sha256 verified" "$out"
check_contains "prints the installed version" "okf $VERSION_NEW+abc1234" "$out"
if [[ -x "$dir/okf" ]]; then ok "installs an executable at \$OKF_INSTALL_DIR/okf"
else bad "installs an executable at \$OKF_INSTALL_DIR/okf" "no executable at $dir/okf"; fi
check_eq "installs the newest version's bytes" \
  "$(sha256sum "$www/v$VERSION_NEW/okf-linux-x64" | cut -d' ' -f1)" \
  "$(sha256sum "$dir/okf" | cut -d' ' -f1)"
check_eq "the installed binary runs" "$VERSION_NEW+abc1234" "$("$dir/okf" version)"
if compgen -G "$dir/.okf.install.*" >/dev/null; then
  bad "leaves no staging file behind" "found $(echo "$dir"/.okf.install.*)"
else ok "leaves no staging file behind"; fi

note "[$sh_bin] PATH hint"
check_contains "hints when the install dir is not on PATH" "is not on your PATH" "$out"
dir_onpath="$work/bin-onpath-$sh_bin"
mkdir -p "$dir_onpath"
set +e
out="$(PATH="$dir_onpath:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir_onpath" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0 when the dir is already on PATH" "0" "$rc"
check_not_contains "stays quiet when the dir is on PATH" "is not on your PATH" "$out"

note "[$sh_bin] idempotent re-run"
dir="$work/bin-happy-$sh_bin"
before="$(sha256sum "$dir/okf" | cut -d' ' -f1)"
run "$sh_bin" "$dir"
check_eq "second run exits 0" "0" "$rc"
check_eq "second run leaves the same bytes" "$before" "$(sha256sum "$dir/okf" | cut -d' ' -f1)"
check_eq "second run leaves exactly one file" "1" "$(find "$dir" -maxdepth 1 -type f | wc -l)"

note "[$sh_bin] --version pins"
dir="$work/bin-pinned-$sh_bin"
run "$sh_bin" "$dir" --version "$VERSION_OLD"
check_eq "exits 0" "0" "$rc"
check_eq "installs the pinned version" "$VERSION_OLD+abc1234" "$("$dir/okf" version)"
check_not_contains "does not install the newest" "$VERSION_NEW+" "$("$dir/okf" version)"

dir="$work/bin-pinned-v-$sh_bin"
run "$sh_bin" "$dir" --version "v$VERSION_OLD"
check_eq "a leading v is tolerated" "0" "$rc"
check_eq "and resolves to the same release" "$VERSION_OLD+abc1234" "$("$dir/okf" version)"

note "[$sh_bin] --dry-run writes nothing"
dir="$work/bin-dry-$sh_bin"
run "$sh_bin" "$dir" --dry-run
check_eq "exits 0" "0" "$rc"
check_contains "says it wrote nothing" "nothing was downloaded or written" "$out"
if [[ -e "$dir" ]]; then bad "creates no install dir" "$dir exists"; else ok "creates no install dir"; fi

note "[$sh_bin] sha256 mismatch is fatal"
tampered="$work/tampered"
mkdir -p "$tampered"
cp -r "$www/." "$tampered/"
printf 'this is not the binary the manifest describes\n' >"$tampered/v$VERSION_NEW/okf-linux-x64"
chmod +x "$tampered/v$VERSION_NEW/okf-linux-x64"
tport=""
for candidate in $(seq 8831 8860); do
  if ! (exec 3<>"/dev/tcp/127.0.0.1/$candidate") 2>/dev/null; then tport="$candidate"; break; fi
done
python3 -m http.server "$tport" --bind 127.0.0.1 --directory "$tampered" >"$work/tampered.log" 2>&1 &
tampered_pid=$!
for _ in $(seq 1 50); do
  curl -fsS "http://127.0.0.1:$tport/latest.json" >/dev/null 2>&1 && break
  sleep 0.1
done
dir="$work/bin-tampered-$sh_bin"
set +e
out="$(OKF_INSTALL_URL="http://127.0.0.1:$tport" OKF_INSTALL_DIR="$dir" "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
kill "$tampered_pid" 2>/dev/null || true
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero"; else bad "exits non-zero" "exited 0"; fi
check_contains "names the failure" "sha256 mismatch" "$out"
# Assert the digests themselves, not the labels: "manifest:" also appears in the ordinary
# info block above, so matching the label alone would pass against an installer that never
# compared anything (AGENTS.md, on assertions that are vacuously satisfiable).
check_contains "prints the digest the manifest promised" \
  "$(sha256sum "$www/v$VERSION_NEW/okf-linux-x64" | cut -d' ' -f1)" "$out"
check_contains "prints the digest it actually got" \
  "$(sha256sum "$tampered/v$VERSION_NEW/okf-linux-x64" | cut -d' ' -f1)" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

note "[$sh_bin] unsupported platform is refused"
# A fake `uname` earlier on PATH, so the real detection code runs rather than a test hook
# compiled into the installer.
fake="$work/fake-arch-$sh_bin"
mkdir -p "$fake"
cat >"$fake/uname" <<'EOF'
#!/bin/sh
case "${1:-}" in
  -s) echo Linux ;;
  -m) echo aarch64 ;;
  *)  echo Linux ;;
esac
EOF
chmod +x "$fake/uname"
dir="$work/bin-arch-$sh_bin"
set +e
out="$(PATH="$fake:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero on aarch64"; else bad "exits non-zero on aarch64" "exited 0"; fi
check_contains "names the architecture it found" "aarch64" "$out"
check_contains "says what it does ship" "linux-x86_64" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

fake_os="$work/fake-os-$sh_bin"
mkdir -p "$fake_os"
cat >"$fake_os/uname" <<'EOF'
#!/bin/sh
case "${1:-}" in
  -s) echo Darwin ;;
  -m) echo arm64 ;;
  *)  echo Darwin ;;
esac
EOF
chmod +x "$fake_os/uname"
dir="$work/bin-os-$sh_bin"
set +e
out="$(PATH="$fake_os:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero on Darwin"; else bad "exits non-zero on Darwin" "exited 0"; fi
check_contains "names the OS it found" "Darwin" "$out"

note "[$sh_bin] a missing release is reported, not guessed at"
dir="$work/bin-missing-$sh_bin"
run "$sh_bin" "$dir" --version 9.9.9
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero"; else bad "exits non-zero" "exited 0"; fi
check_contains "names the URL it could not fetch" "v9.9.9/latest.json" "$out"
check_contains "mentions the local-only caveat" "ringo/okf-net#26" "$out"

note "[$sh_bin] bad usage exits 2"
run "$sh_bin" "$work/bin-usage-$sh_bin" --wat
check_eq "unknown flag exits 2" "2" "$rc"
check_contains "says which flag" "--wat" "$out"
run "$sh_bin" "$work/bin-usage2-$sh_bin" --version
check_contains "--version with no value is rejected" "needs a value" "$out"

done

# ---------------------------------------------------------------------------

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]
