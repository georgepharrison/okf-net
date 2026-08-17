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

# A fake HOME for every installer run below. install.sh writes a shell completion under
# $XDG_DATA_HOME (else $HOME/.local/share) since #51, and a test suite that let that land in
# the developer's real home would be editing the machine it is running on. Exported once,
# here, so no case can forget it.
export HOME="$work/home"
export XDG_DATA_HOME="$work/home/.local/share"
export XDG_CONFIG_HOME="$work/home/.config"
mkdir -p "$XDG_DATA_HOME" "$XDG_CONFIG_HOME"
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
# The fixture: a www tree shaped exactly like the artifact host, with independent stable
# and dev channels plus shared immutable releases. Stable stays at 1.0.0 while dev advances
# to an rc, and an older release makes "newest" and "--version" distinguishable answers.
# ---------------------------------------------------------------------------

www="$work/www"
mkdir -p "$www"

VERSION_NEW="1.0.0"
VERSION_DEV="1.1.0-rc.16"
VERSION_OLD="1.0.0-rc.15"

# Every asset a release carries, in the manifest's own order. The two binaries the
# installer can select between are FIRST, which is what makes the digest assertions below
# meaningful: the sed reader anchors on a greedy `.*`, so an asset map that grows past the
# entry being looked up is exactly how a naive reader starts answering with the LAST
# asset's digest. The Windows binary and install.ps1 are in the fixture too — install.sh
# will never select them, and having them there proves it does not.
make_release() { # make_release <version>
  local v="$1" dir="$www/v$1" name
  mkdir -p "$dir"

  # Stand-ins for the real binaries: scripts that answer `okf version`, so the installer's
  # final "prints the installed version" step is exercised rather than mocked away. The
  # string they answer with is a stand-in too — a released binary prints the bare version
  # since #53 — and what earns its place in it is the asset name. Two of them, with
  # DIFFERENT bytes — that is what makes "did it pick the right asset" an answerable
  # question rather than a coincidence.
  #
  # They also answer `okf skills install`, which the installer runs once the binary is in
  # place (#41). Every invocation is appended to $OKF_STUB_LOG when that is set — the
  # environment is inherited through sh -> install.sh -> the binary — so a test can assert
  # WHICH subcommands the installer called rather than inferring it from an exit code. The
  # log lives outside the install directory on purpose: a file beside `okf` would break the
  # "second run leaves exactly one file" assertion further down.
  #
  # OKF_STUB_SKILLS_FAIL=1 makes the skills step fail, which is how "a failed skills
  # install is a warning, not a failed install" gets exercised without a second fixture
  # release whose digests would all have to be recomputed.
  for name in okf-linux-x64 okf-osx-arm64; do
    cat >"$dir/$name" <<EOF
#!/bin/sh
[ -n "\${OKF_STUB_LOG:-}" ] && printf '%s\n' "\$*" >>"\$OKF_STUB_LOG"
[ "\${1:-}" = version ] && echo "$v+abc1234 ($name)" && exit 0
if [ "\${1:-}" = skills ] && [ "\${2:-}" = install ]; then
  [ "\${OKF_STUB_SKILLS_FAIL:-0}" = 1 ] && echo "okf: error: nope" >&2 && exit 2
  echo "skills installed" && exit 0
fi
if [ "\${1:-}" = completion ]; then
  [ "\${OKF_STUB_COMPLETION_FAIL:-0}" = 1 ] && echo "okf: error: nope" >&2 && exit 2
  echo "# okf completion for \${2:-}" && exit 0
fi
echo "okf $v" && exit 0
EOF
    chmod +x "$dir/$name"
  done

  # Never selected by install.sh; present because a release carries them and the reader
  # has to walk past them. The skills archive is one of these: the installer never fetches
  # it, because every binary already carries the skills (#41).
  printf 'MZ this is not really a PE binary, version %s\n' "$v" >"$dir/okf-win-x64.exe"
  printf 'not really a tarball, version %s\n' "$v" >"$dir/okf-net-knowledge.tar.gz"
  printf 'not really the skills archive, version %s\n' "$v" >"$dir/okf-skills.tar.gz"
  cp "$installer" "$dir/install.sh"
  cp "$repo/install.ps1" "$dir/install.ps1"

  sha_line() { sha256sum "$dir/$1" | cut -d' ' -f1; }

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
      "sha256": "$(sha_line okf-linux-x64)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-linux-x64",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/okf-linux-x64"
    },
    "okf-osx-arm64": {
      "path": "v$v/okf-osx-arm64",
      "size": $(wc -c <"$dir/okf-osx-arm64"),
      "sha256": "$(sha_line okf-osx-arm64)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-osx-arm64",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/okf-osx-arm64"
    },
    "okf-win-x64.exe": {
      "path": "v$v/okf-win-x64.exe",
      "size": $(wc -c <"$dir/okf-win-x64.exe"),
      "sha256": "$(sha_line okf-win-x64.exe)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-win-x64.exe",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/okf-win-x64.exe"
    },
    "okf-net-knowledge.tar.gz": {
      "path": "v$v/okf-net-knowledge.tar.gz",
      "size": $(wc -c <"$dir/okf-net-knowledge.tar.gz"),
      "sha256": "$(sha_line okf-net-knowledge.tar.gz)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-net-knowledge.tar.gz",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/okf-net-knowledge.tar.gz"
    },
    "okf-skills.tar.gz": {
      "path": "v$v/okf-skills.tar.gz",
      "size": $(wc -c <"$dir/okf-skills.tar.gz"),
      "sha256": "$(sha_line okf-skills.tar.gz)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/okf-skills.tar.gz",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/okf-skills.tar.gz"
    },
    "install.sh": {
      "path": "v$v/install.sh",
      "size": $(wc -c <"$dir/install.sh"),
      "sha256": "$(sha_line install.sh)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/install.sh",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/install.sh"
    },
    "install.ps1": {
      "path": "v$v/install.ps1",
      "size": $(wc -c <"$dir/install.ps1"),
      "sha256": "$(sha_line install.ps1)",
      "url": "https://gitlab.tychostation.dev/api/v4/projects/ringo%2Fokf-net/packages/generic/okf/$v/install.ps1",
      "downloadUrl": "https://github.com/georgepharrison/okf-net/releases/download/v$v/install.ps1"
    }
  }
}
EOF
}

make_release "$VERSION_OLD"
make_release "$VERSION_NEW"
make_release "$VERSION_DEV"

# Stable and dev are independent moving trees. The root remains a backwards-compatible
# stable alias for installers published before the split; current clients read the explicit
# channel directories. Immutable releases remain shared under /v<version>/.
channel_release() { # channel_release <channel-dir> <version>
  local channel="$1" version="$2" f
  mkdir -p "$www/$channel"
  for f in latest.json install.sh install.ps1 \
           okf-linux-x64 okf-osx-arm64 okf-win-x64.exe \
           okf-net-knowledge.tar.gz okf-skills.tar.gz; do
    cp "$www/v$version/$f" "$www/$channel/$f"
  done
}

channel_release stable "$VERSION_NEW"
channel_release dev "$VERSION_DEV"
for f in latest.json install.sh install.ps1 \
         okf-linux-x64 okf-osx-arm64 okf-win-x64.exe \
         okf-net-knowledge.tar.gz okf-skills.tar.gz; do
  cp "$www/stable/$f" "$www/$f"
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

# The public default is exercised without a network or a certificate. This curl shim maps
# the public Pages and GitHub Release URLs back to the same fixture server, while leaving
# every curl option and the installer's real download code intact.
public_shim="$work/public-curl"
mkdir -p "$public_shim"
cat >"$public_shim/curl" <<EOF
#!/bin/bash
args=()
skip=0
for arg in "\$@"; do
  if [ "\$skip" -eq 1 ]; then skip=0; continue; fi
  case "\$arg" in
    --proto|--proto-redir) skip=1; continue ;;
    https://georgepharrison.github.io/okf-net/stable/*|https://georgepharrison.github.io/okf-net/dev/*|https://georgepharrison.github.io/okf-net/v*)
      arg="${base}\${arg#https://georgepharrison.github.io/okf-net}" ;;
    https://github.com/georgepharrison/okf-net/releases/download/v*/*)
      release_path="\${arg#https://github.com/georgepharrison/okf-net/releases/download/}"
      tag="\${release_path%%/*}"
      name="\${release_path#*/}"
      arg="${base}/\${tag}/\${name}" ;;
  esac
  args+=("\$arg")
done
exec $(command -v curl) "\${args[@]}"
EOF
chmod +x "$public_shim/curl"

# ---------------------------------------------------------------------------
# run <shell> <install-dir> [args...] — invoke the installer, capture rc + merged output.
# ---------------------------------------------------------------------------

out=""
rc=0
stub_log=""
run() {
  local shell="$1" dir="$2"; shift 2
  stub_log="$work/stub-$(basename "$dir").log"
  : >"$stub_log"
  set +e
  out="$(OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" OKF_STUB_LOG="$stub_log" \
         "$shell" "$installer" "$@" 2>&1)"
  rc=$?
  set -e
}

shells=(sh)
if command -v dash >/dev/null 2>&1; then
  shells+=(dash)
fi
echo "shells under test: ${shells[*]}"

for sh_bin in "${shells[@]}"; do

note "[$sh_bin] public default uses downloadUrl"
public_dir="$work/bin-public-$sh_bin"
public_stub_log="$work/stub-public-$sh_bin.log"
: >"$public_stub_log"
set +e
out="$(env -u OKF_INSTALL_URL OKF_INSTALL_DIR="$public_dir" OKF_STUB_LOG="$public_stub_log" \
       PATH="$public_shim:$PATH" "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "public default exits 0" "0" "$rc"
check_contains "public default reads the Pages manifest" "https://georgepharrison.github.io/okf-net/stable/latest.json" "$out"
check_contains "public default uses the GitHub downloadUrl" "https://github.com/georgepharrison/okf-net/releases/download/v$VERSION_NEW/okf-linux-x64" "$out"
check_eq "public default installs the release bytes" "$(cat "$www/v$VERSION_NEW/okf-linux-x64")" "$(cat "$public_dir/okf")"

note "[$sh_bin] happy path"
dir="$work/bin-happy-$sh_bin"
run "$sh_bin" "$dir"
check_eq   "exits 0" "0" "$rc"
check_contains "reports the resolved stable version" "$VERSION_NEW" "$out"
check_contains "reads the explicit stable channel" "$base/stable/latest.json" "$out"
check_contains "verifies the digest" "sha256 verified" "$out"
check_contains "prints the installed version" "okf $VERSION_NEW+abc1234" "$out"
if [[ -x "$dir/okf" ]]; then ok "installs an executable at \$OKF_INSTALL_DIR/okf"
else bad "installs an executable at \$OKF_INSTALL_DIR/okf" "no executable at $dir/okf"; fi
check_eq "installs the newest version's bytes" \
  "$(sha256sum "$www/v$VERSION_NEW/okf-linux-x64" | cut -d' ' -f1)" \
  "$(sha256sum "$dir/okf" | cut -d' ' -f1)"
check_eq "the installed binary runs, and is the linux-x64 asset" \
  "$VERSION_NEW+abc1234 (okf-linux-x64)" "$("$dir/okf" version)"
if compgen -G "$dir/.okf.install.*" >/dev/null; then
  bad "leaves no staging file behind" "found $(echo "$dir"/.okf.install.*)"
else ok "leaves no staging file behind"; fi

note "[$sh_bin] release-candidate channel"
dir="$work/bin-rc-$sh_bin"
run "$sh_bin" "$dir" --channel rc
check_eq "explicit rc install exits 0" "0" "$rc"
check_contains "reads the dev channel manifest" "$base/dev/latest.json" "$out"
check_eq "installs the newest rc instead of stable" \
  "$VERSION_DEV+abc1234 (okf-linux-x64)" "$("$dir/okf" version)"
rc_before="$(sha256sum "$dir/okf" | cut -d' ' -f1)"
run "$sh_bin" "$dir" --channel=rc
check_eq "rc re-run exits 0" "0" "$rc"
check_eq "rc re-run leaves the same bytes" "$rc_before" \
  "$(sha256sum "$dir/okf" | cut -d' ' -f1)"

note "[$sh_bin] the upgrade hint"
# A person who just installed okf should not have to discover `okf upgrade` by reading the
# README; the installer is the last place they will be looking. Asserted on the happy
# path's output, which is still in $out.
check_contains "names the verb that replaces this script next time" "okf upgrade" "$out"
check_contains "and the flag that only reports" "okf upgrade --check" "$out"

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

# Two spellings of "the same directory" that a string comparison gets wrong, and both are
# ordinary: `PATH` entries pick up trailing slashes, and `~/.local/bin` is very often a
# symlink into a dotfiles checkout. Getting these wrong prints an `export PATH=…` the
# reader does not need and that will not help them, which is worse than saying nothing.
dir_slash="$work/bin-pathslash-$sh_bin"
mkdir -p "$dir_slash"
set +e
out="$(PATH="$dir_slash/:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir_slash" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0 with a trailing slash on the PATH entry" "0" "$rc"
check_not_contains "a trailing slash on the PATH entry is still the same dir" \
  "is not on your PATH" "$out"

real_dir="$work/bin-pathreal-$sh_bin"
link_dir="$work/bin-pathlink-$sh_bin"
mkdir -p "$real_dir"
ln -sfn "$real_dir" "$link_dir"
set +e
out="$(PATH="$real_dir:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$link_dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0 installing through a symlinked dir" "0" "$rc"
check_not_contains "a symlinked install dir is still the dir it points at" \
  "is not on your PATH" "$out"

note "[$sh_bin] the installer installs the agent skills"
# The skills ship inside the binary (#41), so the installer runs `okf skills install`
# instead of downloading anything. The stub records its own argv, which is what makes
# "the installer called it, with those arguments" answerable rather than inferred.
dir="$work/bin-skills-$sh_bin"
run "$sh_bin" "$dir"
check_eq "exits 0" "0" "$rc"
check_contains "runs \`okf skills install\`" "skills install" "$(cat "$stub_log")"
check_contains "and says what it is doing" "installing the agent skills" "$out"

dir="$work/bin-skip-skills-$sh_bin"
skip_log="$work/skip-skills-$sh_bin.log"
: >"$skip_log"
set +e
out="$(OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" OKF_STUB_LOG="$skip_log" \
       OKF_SKIP_SKILLS=1 "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "OKF_SKIP_SKILLS=1 still exits 0" "0" "$rc"
check_not_contains "OKF_SKIP_SKILLS=1 calls no skills subcommand" "skills install" "$(cat "$skip_log")"
check_contains "but the binary was still installed and asked its version" "version" "$(cat "$skip_log")"
check_contains "and the run says it skipped them" "OKF_SKIP_SKILLS=1" "$out"

# A failed skills step must not fail an install that already landed verified bytes: the
# binary works, and the skills are one command away.
dir="$work/bin-skills-fail-$sh_bin"
set +e
out="$(OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" OKF_STUB_SKILLS_FAIL=1 \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "a failed skills install still exits 0" "0" "$rc"
check_contains "warns instead of failing" "could not install the agent skills" "$out"
check_contains "and names the command to re-run" "$dir/okf skills install" "$out"
if [[ -x "$dir/okf" ]]; then ok "and the binary is installed anyway"
else bad "and the binary is installed anyway" "no executable at $dir/okf"; fi

note "[$sh_bin] the installer places the shell completion"
# The completion script comes out of the INSTALLED binary (#51), so the stub prints it and
# the test asserts the bytes that landed. Every case gets its own XDG roots, which is what
# makes "no file was written" an answerable question rather than an absence of evidence.
#
# $SHELL is the only detector: the shell running install.sh is `sh` in every one of these
# runs, and it is `sh` on a real `curl … | sh` too.
completion_case() { # completion_case <label> <shell> <data|config> <relative file> [extra env...]
  local label="$1" login_shell="$2" root="$3" relative="$4"; shift 4
  comp_root="$work/comp-$label-$sh_bin"
  comp_file="$comp_root/$root/$relative"
  set +e
  out="$(env OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$comp_root/bin" \
             XDG_DATA_HOME="$comp_root/data" XDG_CONFIG_HOME="$comp_root/config" \
             SHELL="$login_shell" "$@" "$sh_bin" "$installer" 2>&1)"
  rc=$?
  set -e
}

completion_case zsh /bin/zsh data "zsh/site-functions/_okf"
check_eq "SHELL=/bin/zsh exits 0" "0" "$rc"
if [[ -f "$comp_file" ]]; then ok "SHELL=/bin/zsh writes _okf to the site-functions dir"
else bad "SHELL=/bin/zsh writes _okf to the site-functions dir" "no file at $comp_file"; fi
check_eq "and the file is what the installed binary printed" \
  "# okf completion for zsh" "$(cat "$comp_file" 2>/dev/null)"
check_contains "and zsh gets the fpath line, which it needs and bash does not" \
  "fpath=(" "$out"
if compgen -G "$comp_file".install.* >/dev/null; then
  bad "and leaves no staging file behind" "found $(echo "$comp_file".install.*)"
else ok "and leaves no staging file behind"; fi

completion_case bash /bin/bash data "bash-completion/completions/okf"
check_eq "SHELL=/bin/bash exits 0" "0" "$rc"
if [[ -f "$comp_file" ]]; then ok "SHELL=/bin/bash writes okf to the bash-completion dir"
else bad "SHELL=/bin/bash writes okf to the bash-completion dir" "no file at $comp_file"; fi
check_eq "and asked the binary for the bash script, not some other shell's" \
  "# okf completion for bash" "$(cat "$comp_file" 2>/dev/null)"
check_not_contains "and no fpath line, which means nothing to bash" "fpath=(" "$out"

completion_case fish /usr/bin/fish config "fish/completions/okf.fish"
check_eq "SHELL=/usr/bin/fish exits 0" "0" "$rc"
check_eq "and fish gets its file under XDG_CONFIG_HOME, where fish looks" \
  "# okf completion for fish" "$(cat "$comp_file" 2>/dev/null)"

completion_case skip /bin/zsh data "zsh/site-functions/_okf" OKF_SKIP_COMPLETIONS=1
check_eq "OKF_SKIP_COMPLETIONS=1 still exits 0" "0" "$rc"
if [[ -e "$comp_file" ]]; then bad "OKF_SKIP_COMPLETIONS=1 writes nothing" "found $comp_file"
else ok "OKF_SKIP_COMPLETIONS=1 writes nothing"; fi
check_contains "and the run says it skipped them" "OKF_SKIP_COMPLETIONS=1" "$out"

# A shell okf has no script for is not an error and not a warning: the user did not ask for
# a completion, and a message about a shell they do not use is noise.
completion_case unknown /bin/ksh data "zsh/site-functions/_okf"
check_eq "an unknown \$SHELL still exits 0" "0" "$rc"
if find "$comp_root/data" "$comp_root/config" -type f 2>/dev/null | grep -q .; then
  bad "an unknown \$SHELL writes no completion" "found $(find "$comp_root/data" "$comp_root/config" -type f 2>/dev/null)"
else ok "an unknown \$SHELL writes no completion"; fi
check_not_contains "and says nothing about completions" "completion" "$out"

# The same reading as the skills step: the bytes are installed and verified by now, so a
# completion that did not land is a warning with the command to fix it.
completion_case failure /bin/bash data "bash-completion/completions/okf" OKF_STUB_COMPLETION_FAIL=1
check_eq "a failed completion write still exits 0" "0" "$rc"
check_contains "warns instead of failing" "could not write the bash completion" "$out"
check_contains "and names the command to re-run" "completion bash >" "$out"
if [[ -x "$comp_root/bin/okf" ]]; then ok "and the binary is installed anyway"
else bad "and the binary is installed anyway" "no executable at $comp_root/bin/okf"; fi

note "[$sh_bin] a trailing slash on the base URL"
dir="$work/bin-baseslash-$sh_bin"
set +e
out="$(OKF_INSTALL_URL="$base/" OKF_INSTALL_DIR="$dir" "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0" "0" "$rc"
check_not_contains "does not build a doubled slash into the URLs it reports" "$base//" "$out"
if [[ -x "$dir/okf" ]]; then ok "still installs"; else bad "still installs" "no executable at $dir/okf"; fi

# More than one of them, because `${VAR%/}` strips exactly one and a base URL pasted with
# `//` on the end is ordinary. install.ps1's `TrimEnd('/')` strips all of them, and the two
# installers must not disagree about what the same environment variable means.
dir="$work/bin-baseslashes-$sh_bin"
set +e
out="$(OKF_INSTALL_URL="$base///" OKF_INSTALL_DIR="$dir" "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0 with several trailing slashes" "0" "$rc"
check_not_contains "trims every trailing slash, not just the last" "$base//" "$out"
check_contains "and still names the manifest it fetched" "$base/stable/latest.json" "$out"
if [[ -x "$dir/okf" ]]; then ok "still installs"; else bad "still installs" "no executable at $dir/okf"; fi

note "[$sh_bin] https stays https across redirects"
# The installer follows redirects, which means one 302 to `http://` would fetch the
# manifest AND the binary in cleartext — and a sha256 checked against a manifest that
# came down the same cleartext channel proves nothing at all. The assertion is on the
# flags the installer hands curl, because the alternative — standing up a TLS endpoint
# that redirects to http — would be testing curl rather than install.sh. A recording shim
# earlier on PATH captures the argv; the run itself fails (the host does not resolve, by
# design) and that is fine, the argv is the artifact under test.
shim="$work/shim-$sh_bin"
mkdir -p "$shim"
argv_log="$work/curl-argv-$sh_bin.log"
cat >"$shim/curl" <<EOF
#!/bin/sh
printf '%s\n' "\$*" >>"$argv_log"
exec $(command -v curl) "\$@"
EOF
chmod +x "$shim/curl"

: >"$argv_log"
set +e
PATH="$shim:$PATH" OKF_INSTALL_URL="https://install-sh-acceptance.invalid" \
  OKF_INSTALL_DIR="$work/bin-tls-$sh_bin" "$sh_bin" "$installer" >/dev/null 2>&1
set -e
check_contains "an https base refuses a plaintext redirect" "--proto-redir =https" "$(cat "$argv_log")"
check_contains "an https base refuses a plaintext first hop too" "--proto =https" "$(cat "$argv_log")"

: >"$argv_log"
set +e
PATH="$shim:$PATH" OKF_INSTALL_URL="$base" \
  OKF_INSTALL_DIR="$work/bin-tls-plain-$sh_bin" "$sh_bin" "$installer" >/dev/null 2>&1
set -e
check_not_contains "an http base does not pin a scheme it is not using" \
  "--proto-redir" "$(cat "$argv_log")"
check_not_contains "and nothing anywhere turns certificate verification off" \
  "--insecure" "$(cat "$argv_log")"

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
check_eq "installs the pinned version" "$VERSION_OLD+abc1234 (okf-linux-x64)" "$("$dir/okf" version)"
check_not_contains "does not install the newest" "$VERSION_NEW+" "$("$dir/okf" version)"

dir="$work/bin-pinned-v-$sh_bin"
run "$sh_bin" "$dir" --version "v$VERSION_OLD"
check_eq "a leading v is tolerated" "0" "$rc"
check_eq "and resolves to the same release" \
  "$VERSION_OLD+abc1234 (okf-linux-x64)" "$("$dir/okf" version)"

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

fake_bsd="$work/fake-bsd-$sh_bin"
mkdir -p "$fake_bsd"
cat >"$fake_bsd/uname" <<'EOF'
#!/bin/sh
case "${1:-}" in
  -s) echo FreeBSD ;;
  -m) echo amd64 ;;
  *)  echo FreeBSD ;;
esac
EOF
chmod +x "$fake_bsd/uname"
dir="$work/bin-bsd-$sh_bin"
set +e
out="$(PATH="$fake_bsd:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero on an OS with no build"; else bad "exits non-zero on an OS with no build" "exited 0"; fi
check_contains "names the OS it found" "FreeBSD" "$out"
check_contains "points Windows readers at the PowerShell installer" "install.ps1" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

# ---------------------------------------------------------------------------
# macOS (work item #36). The same fake-`uname`-on-PATH trick as above, so the shipped
# detection code is what runs; there is no test hook in the installer and there should not
# be. The fixture's two binaries carry DIFFERENT bytes and print their own asset name, so
# "installed the macOS asset" is asserted against the bytes and the output, not inferred
# from an exit code.
#
# What this cannot cover is macOS itself: Gatekeeper, a real Mach-O, and a real `xattr`.
# The quarantine case below asserts the CALL — a recording shim named `xattr` earlier on
# PATH — which is the same technique the TLS cases use for curl's argv, and for the same
# reason: the alternative is testing Apple rather than testing install.sh.
# ---------------------------------------------------------------------------

note "[$sh_bin] macOS arm64 gets the osx-arm64 asset"
fake_mac="$work/fake-mac-$sh_bin"
mkdir -p "$fake_mac"
cat >"$fake_mac/uname" <<'EOF'
#!/bin/sh
case "${1:-}" in
  -s) echo Darwin ;;
  -m) echo arm64 ;;
  *)  echo Darwin ;;
esac
EOF
chmod +x "$fake_mac/uname"

dir="$work/bin-mac-$sh_bin"
set +e
out="$(PATH="$fake_mac:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0 on Darwin/arm64" "0" "$rc"
check_contains "names the macOS asset" "okf-osx-arm64" "$out"
check_not_contains "does not reach for the linux asset" "okf-linux-x64" "$out"
check_contains "still verifies the digest" "sha256 verified" "$out"
if [[ -x "$dir/okf" ]]; then ok "installs an executable"; else bad "installs an executable" "no executable at $dir/okf"; fi
check_eq "installs the osx-arm64 bytes, not the linux ones" \
  "$(sha256sum "$www/v$VERSION_NEW/okf-osx-arm64" | cut -d' ' -f1)" \
  "$(sha256sum "$dir/okf" | cut -d' ' -f1)"
check_contains "the installed binary reports the macOS asset" \
  "$VERSION_NEW+abc1234 (okf-osx-arm64)" "$("$dir/okf" version)"

note "[$sh_bin] macOS: the sha256 check is not relaxed"
# The whole verification path has to survive the platform branch. Same tampered tree as
# the Linux case, but with the osx asset replaced.
mac_tampered="$work/mac-tampered-$sh_bin"
mkdir -p "$mac_tampered"
cp -r "$www/." "$mac_tampered/"
printf 'this is not the macOS binary the manifest describes\n' >"$mac_tampered/v$VERSION_NEW/okf-osx-arm64"
chmod +x "$mac_tampered/v$VERSION_NEW/okf-osx-arm64"
mport=""
for candidate in $(seq 8861 8890); do
  if ! (exec 3<>"/dev/tcp/127.0.0.1/$candidate") 2>/dev/null; then mport="$candidate"; break; fi
done
[[ -n "$mport" ]] || { echo "no free port in 8861-8890" >&2; exit 2; }
python3 -m http.server "$mport" --bind 127.0.0.1 --directory "$mac_tampered" >"$work/mac-tampered.log" 2>&1 &
mac_pid=$!
for _ in $(seq 1 50); do
  curl -fsS "http://127.0.0.1:$mport/latest.json" >/dev/null 2>&1 && break
  sleep 0.1
done
dir="$work/bin-mac-tampered-$sh_bin"
set +e
out="$(PATH="$fake_mac:$PATH" OKF_INSTALL_URL="http://127.0.0.1:$mport" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
kill "$mac_pid" 2>/dev/null || true
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero"; else bad "exits non-zero" "exited 0"; fi
check_contains "names the failure" "sha256 mismatch" "$out"
check_contains "prints the digest the manifest promised" \
  "$(sha256sum "$www/v$VERSION_NEW/okf-osx-arm64" | cut -d' ' -f1)" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

note "[$sh_bin] macOS: the quarantine attribute is removed"
# Gatekeeper refuses to run an unsigned, un-notarised binary that carries
# com.apple.quarantine — which curl sets on everything it downloads — so the installer
# strips it. A recording shim proves the call was made and made against the installed
# file; nothing here can prove Gatekeeper is satisfied.
mkdir -p "$work/shim-xattr-$sh_bin"
cp "$fake_mac/uname" "$work/shim-xattr-$sh_bin/uname"
xattr_log="$work/xattr-argv-$sh_bin.log"
cat >"$work/shim-xattr-$sh_bin/xattr" <<EOF
#!/bin/sh
printf '%s\n' "\$*" >>"$xattr_log"
exit 0
EOF
chmod +x "$work/shim-xattr-$sh_bin/xattr"
: >"$xattr_log"
dir="$work/bin-mac-xattr-$sh_bin"
set +e
out="$(PATH="$work/shim-xattr-$sh_bin:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
check_eq "exits 0" "0" "$rc"
check_contains "removes com.apple.quarantine" "-d com.apple.quarantine" "$(cat "$xattr_log")"
check_contains "removes it from the installed binary, not the staging copy" \
  "$dir/okf" "$(cat "$xattr_log")"
check_not_contains "and leaves the staging copy out of it" ".okf.install." "$(cat "$xattr_log")"

# The same run on Linux must not go anywhere near xattr: the attribute is Apple's, and a
# Linux box that happens to have an `xattr` on PATH is not a reason to touch a file's
# extended attributes. The shim is the same recorder, with the fake `uname` left out.
linux_xattr="$work/shim-xattr-linux-$sh_bin"
mkdir -p "$linux_xattr"
cp "$work/shim-xattr-$sh_bin/xattr" "$linux_xattr/xattr"
: >"$xattr_log"
set +e
PATH="$linux_xattr:$PATH" OKF_INSTALL_URL="$base" \
  OKF_INSTALL_DIR="$work/bin-linux-xattr-$sh_bin" "$sh_bin" "$installer" >/dev/null 2>&1
set -e
check_eq "Linux never calls xattr, even when one is on PATH" "" "$(cat "$xattr_log")"

note "[$sh_bin] an Intel Mac is refused, by name, with somewhere to ask"
fake_intel="$work/fake-intel-$sh_bin"
mkdir -p "$fake_intel"
cat >"$fake_intel/uname" <<'EOF'
#!/bin/sh
case "${1:-}" in
  -s) echo Darwin ;;
  -m) echo x86_64 ;;
  *)  echo Darwin ;;
esac
EOF
chmod +x "$fake_intel/uname"
dir="$work/bin-intel-$sh_bin"
set +e
out="$(PATH="$fake_intel:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero on Darwin/x86_64"; else bad "exits non-zero on Darwin/x86_64" "exited 0"; fi
check_contains "names what it found" "Darwin/x86_64" "$out"
check_contains "names the issue to ask on" "issues/36" "$out"
check_not_contains "does not silently install the arm64 build" "sha256 verified" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

# The same `uname -m`, a different machine. An Apple Silicon Mac running a translated
# shell — an x86_64 Terminal, or an x86_64 Homebrew, both ordinary — answers x86_64 too,
# and telling that owner their machine has no build is wrong advice they would believe.
# `hw.optional.arm64` is the hardware talking rather than the process.
fake_rosetta="$work/fake-rosetta-$sh_bin"
mkdir -p "$fake_rosetta"
cp "$fake_intel/uname" "$fake_rosetta/uname"
cat >"$fake_rosetta/sysctl" <<'EOF'
#!/bin/sh
[ "${1:-}" = -n ] && [ "${2:-}" = hw.optional.arm64 ] && echo 1 && exit 0
exit 1
EOF
chmod +x "$fake_rosetta/sysctl"
dir="$work/bin-rosetta-$sh_bin"
set +e
out="$(PATH="$fake_rosetta:$PATH" OKF_INSTALL_URL="$base" OKF_INSTALL_DIR="$dir" \
       "$sh_bin" "$installer" 2>&1)"
rc=$?
set -e
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero under Rosetta"; else bad "exits non-zero under Rosetta" "exited 0"; fi
check_contains "says the machine is Apple Silicon, not that it is unsupported" \
  "Apple Silicon Mac" "$out"
check_contains "tells them how to re-run natively" "arch -arm64" "$out"
check_not_contains "does not send an M-series owner to ask for an Intel build" \
  "no Intel-Mac build" "$out"
if [[ -e "$dir/okf" ]]; then bad "installs nothing" "$dir/okf exists"; else ok "installs nothing"; fi

note "[$sh_bin] a missing release is reported, not guessed at"
dir="$work/bin-missing-$sh_bin"
run "$sh_bin" "$dir" --version 9.9.9
if [[ "$rc" -ne 0 ]]; then ok "exits non-zero"; else bad "exits non-zero" "exited 0"; fi
check_contains "names the URL it could not fetch" "v9.9.9/latest.json" "$out"
check_contains "mentions the public Pages and mirror escape hatch" "OKF_INSTALL_URL" "$out"

note "[$sh_bin] bad usage exits 2"
run "$sh_bin" "$work/bin-usage-$sh_bin" --wat
check_eq "unknown flag exits 2" "2" "$rc"
check_contains "says which flag" "--wat" "$out"
run "$sh_bin" "$work/bin-usage2-$sh_bin" --version
check_contains "--version with no value is rejected" "needs a value" "$out"
run "$sh_bin" "$work/bin-usage3-$sh_bin" --channel nightly
check_eq "an unknown channel exits 2" "2" "$rc"
check_contains "an unknown channel names the accepted values" "stable or rc" "$out"
run "$sh_bin" "$work/bin-usage4-$sh_bin" --version 1.0.0 --channel stable
check_eq "a pinned version and a channel exit 2" "2" "$rc"
check_contains "a pinned version leaves the channel nothing to choose" "give one or the other" "$out"

done

# ---------------------------------------------------------------------------

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]
