#!/bin/sh
# okf installer, for Linux and macOS. On Windows, use install.ps1.
#
#   curl -fsSL https://get.okf.tychostation.dev/install.sh | sh
#
# Downloads the newest `okf` release, verifies it against the release manifest, installs
# it to ~/.local/bin/okf, runs `okf skills install` to place the agent skills the binary
# carries, and writes the completion script for the shell $SHELL names. Re-running is safe:
# it reinstalls the same version over itself rather than accumulating anything.
#
# This file is served BY a release as well as living in the repository: the tag pipeline
# uploads it alongside the binary, so the installer a release hands you is the one that
# release was built with. See docs/decisions.md.
#
# Usage:
#   install.sh                      install the newest stable release
#   install.sh --channel rc         install the newest release candidate
#   install.sh --version 1.0.0
#   install.sh --dry-run            say what would happen, write nothing
#   install.sh --help
#
# Environment:
#   OKF_INSTALL_URL   base URL to install from   (default https://get.okf.tychostation.dev)
#   OKF_INSTALL_DIR   directory to install into  (default $HOME/.local/bin)
#   OKF_SKIP_SKILLS   set to 1 to install the binary only, no agent skills
#   OKF_SKIP_COMPLETIONS  set to 1 to skip the shell completion for your $SHELL
#
# POSIX sh on purpose — no bashisms. The one thing an installer may not assume is a shell,
# and `curl | sh` on a Debian box runs dash.

set -eu

# Trailing slashes trimmed here, because every URL below is built by appending to this and
# `https://host//latest.json` is a different URL to most caches and some servers. ALL of
# them, not one: `${VAR%/}` strips a single slash, so a base URL ending `//` — an ordinary
# copy-paste — would still build the doubled path this exists to prevent. install.ps1 uses
# `TrimEnd('/')`, which strips all of them, and the two installers should not disagree
# about what the same environment variable means.
OKF_BASE_URL="${OKF_INSTALL_URL:-https://get.okf.tychostation.dev}"
while [ "${OKF_BASE_URL%/}" != "$OKF_BASE_URL" ]; do
  OKF_BASE_URL="${OKF_BASE_URL%/}"
done
OKF_DIR="${OKF_INSTALL_DIR:-$HOME/.local/bin}"

version=""
channel="stable"
channel_given=0
dry_run=0

usage() {
  cat <<'EOF'
usage: install.sh [--version <version> | --channel <stable|rc>] [--dry-run]

  --version <v>   install this release instead of the newest (e.g. 1.0.0,
                  with or without a leading `v`)
  --channel <c>   install from stable/main or rc/dev (default: stable)
  --dry-run       report what would be downloaded and installed; write nothing
  -h, --help      this message

environment:
  OKF_INSTALL_URL   base URL to install from  (default https://get.okf.tychostation.dev)
  OKF_INSTALL_DIR   install directory         (default $HOME/.local/bin)
  OKF_SKIP_SKILLS   set to 1 to skip `okf skills install`
  OKF_SKIP_COMPLETIONS  set to 1 to skip the shell completion for your $SHELL
EOF
}

say()  { printf '%s\n' "$*"; }
warn() { printf '%s\n' "$*" >&2; }
die()  { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --version)
      [ $# -ge 2 ] || die "--version needs a value"
      version="${2#v}"
      shift 2
      ;;
    --version=*)
      version="${1#--version=}"
      version="${version#v}"
      shift
      ;;
    --channel)
      [ $# -ge 2 ] || die "--channel needs a value"
      channel="$2"
      channel_given=1
      shift 2
      ;;
    --channel=*)
      channel="${1#--channel=}"
      channel_given=1
      shift
      ;;
    --dry-run) dry_run=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) warn "install.sh: unknown argument: $1"; usage >&2; exit 2 ;;
  esac
done

case "$channel" in
  stable|rc) ;;
  *)
    warn "install.sh: unknown --channel value: $channel (expected stable or rc)"
    usage >&2
    exit 2
    ;;
esac

if [ -n "$version" ] && [ "$channel_given" -eq 1 ]; then
  warn "install.sh: --version pins one release, so --channel has nothing left to choose; give one or the other"
  usage >&2
  exit 2
fi

# ---------------------------------------------------------------------------
# Platform
#
# Which asset this machine gets is decided HERE and nowhere else: everything downstream
# reads $OKF_ASSET. Two builds ship (#36):
#
#   Linux  x86_64  ->  okf-linux-x64   NativeAOT, ~6 MB
#   Darwin arm64   ->  okf-osx-arm64   trim-safe self-contained, ~15 MB
#
# Anything else is refused BY NAME, and that is the point of doing it first. An installer
# that downloads a linux-x64 binary onto an arm64 Mac and then fails with "cannot execute
# binary file" has told the user nothing about why.
#
# Windows is not reachable from here — a stock Windows box has no `uname` and no `sh` —
# so the Windows path is install.ps1, and the catch-all below says so rather than leaving
# a reader to guess.
# ---------------------------------------------------------------------------

os="$(uname -s 2>/dev/null || echo unknown)"
arch="$(uname -m 2>/dev/null || echo unknown)"

case "${os}/${arch}" in
  Linux/x86_64|Linux/amd64)
    OKF_ASSET="okf-linux-x64"
    ;;
  Darwin/arm64|Darwin/aarch64)
    OKF_ASSET="okf-osx-arm64"
    ;;
  Darwin/x86_64)
    # TWO different machines answer `uname -m` this way, and they need opposite advice:
    # a real Intel Mac, and an Apple Silicon Mac whose shell happens to be running
    # translated (an x86_64 Terminal, or an x86_64 Homebrew, both of which are ordinary
    # and neither of which announces itself). `hw.optional.arm64` is the hardware talking
    # rather than the process, so it tells them apart. Telling an M-series owner that
    # their machine has no build would be wrong, and they would believe it.
    if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then
      die "this is an Apple Silicon Mac, but the shell running install.sh is x86_64
    — Rosetta. okf ships an arm64 macOS build and no x86_64 one, so re-run this
    under a native shell:
        arch -arm64 /bin/sh -c 'curl -fsSL ${OKF_BASE_URL}/install.sh | sh'"
    fi
    # A real Intel Mac. Rosetta cannot help — it translates x86_64 to arm64, not the
    # other way — so there is nothing to fall back to, and saying "no macOS build" would
    # be wrong as well as unhelpful. An osx-x64 asset is one more runtime identifier on
    # the same publish job, so this is a question of whether anyone needs it.
    die "okf has no Intel-Mac build. This machine is Darwin/x86_64; the macOS build
    that ships is Apple Silicon (osx-arm64), and Rosetta translates the wrong way.
    Adding osx-x64 is one more line in the publish job — ask for it at
    https://gitlab.tychostation.dev/ringo/okf-net/-/issues/36
    Meanwhile, build from source: https://gitlab.tychostation.dev/ringo/okf-net"
    ;;
  Linux/*)
    die "okf ships a linux-x86_64 binary for Linux; this machine is ${arch}.
    There is no ${arch} build yet. Build from source instead:
    https://gitlab.tychostation.dev/ringo/okf-net"
    ;;
  *)
    die "okf ships Linux x86_64 and macOS arm64 binaries; this is ${os} (${arch}).
    On Windows, use the PowerShell installer instead:
        irm https://get.okf.tychostation.dev/install.ps1 | iex
    Otherwise build from source: https://gitlab.tychostation.dev/ringo/okf-net"
    ;;
esac

# ---------------------------------------------------------------------------
# Tools
# ---------------------------------------------------------------------------

have() { command -v "$1" >/dev/null 2>&1; }

if have curl; then
  downloader=curl
elif have wget; then
  downloader=wget
else
  die "need curl or wget to download anything"
fi

if have sha256sum; then
  sha_tool=sha256sum
elif have shasum; then
  sha_tool=shasum
else
  die "need sha256sum or shasum to verify the download"
fi

# TLS. Certificates are verified against the system trust store — nothing here passes
# `--insecure` or `--no-check-certificate`, and nothing pins a certificate or a key, so an
# artifact host can rotate its own without reissuing this script.
#
# Redirects ARE followed: the artifact host is allowed to move, and #26 may well end up
# behind something that redirects. But when the base URL is https, they are followed only
# to https. Without that, one 302 to `http://` fetches the manifest AND the binary in
# cleartext from whoever answered — and a sha256 compared against a manifest that came
# down the same cleartext channel proves nothing whatsoever. `--proto` covers the first
# hop, `--proto-redir` every hop after it; wget's `--https-only` is both at once.
#
# An http base URL is left alone deliberately, because the acceptance suite serves the
# fixture over plain http on localhost. Pinning the scheme to whatever the caller asked
# for is the honest rule: no silent upgrade, and no silent downgrade either.
case "$OKF_BASE_URL" in
  https://*) tls_only=1 ;;
  *)         tls_only=0 ;;
esac

download() { # download <url> <dest>
  case "$downloader" in
    curl)
      if [ "$tls_only" -eq 1 ]; then
        curl --fail --silent --show-error --location \
             --proto '=https' --proto-redir '=https' --output "$2" "$1"
      else
        curl --fail --silent --show-error --location --output "$2" "$1"
      fi
      ;;
    wget)
      if [ "$tls_only" -eq 1 ]; then
        wget --quiet --https-only --output-document "$2" "$1"
      else
        wget --quiet --output-document "$2" "$1"
      fi
      ;;
  esac
}

sha256_of() { # sha256_of <file>
  case "$sha_tool" in
    sha256sum) sha256sum "$1" ;;
    shasum)    shasum -a 256 "$1" ;;
  esac | cut -d' ' -f1
}

# ---------------------------------------------------------------------------
# Manifest
#
# Read with sed rather than jq or python3: an installer piped into `sh` on a fresh box may
# have neither, and needing one would trade the whole point of a single-command install for
# three scalar strings. That is affordable only because okf-net generates this file: every
# value in it (a semver, a relative path, a hex digest, an RFC3339 stamp) is guaranteed
# free of whitespace and of quotes, and no asset object nests another. `tr -d` then flattens
# the pretty-printing away, so the reader does not depend on how the JSON is laid out.
#
# The corresponding obligation is on the producer: the `publish` job in .gitlab-ci.yml must
# keep those guarantees, and tests/install-sh covers the reader against a manifest written
# the way that job writes it.
# ---------------------------------------------------------------------------

manifest_flat() { # manifest_flat <file>
  tr -d ' \t\n\r' <"$1"
}

manifest_version() { # manifest_version <file>
  manifest_flat "$1" | sed -n 's/.*"version":"\([^"]*\)".*/\1/p'
}

manifest_asset() { # manifest_asset <file> <asset-name> <field>
  manifest_flat "$1" \
    | sed -n 's/.*"'"$2"'":{\([^}]*\)}.*/\1/p' \
    | sed -n 's/.*"'"$3"'":"\([^"]*\)".*/\1/p'
}

# ---------------------------------------------------------------------------

tmp="$(mktemp -d "${TMPDIR:-/tmp}/okf-install.XXXXXX")" || die "could not create a temp dir"
trap 'rm -rf "$tmp"' EXIT INT TERM

if [ -n "$version" ]; then
  manifest_url="${OKF_BASE_URL}/v${version}/latest.json"
  say "==> okf ${version}"
else
  case "$channel" in
    stable) channel_directory=stable ;;
    rc)     channel_directory=dev ;;
  esac
  manifest_url="${OKF_BASE_URL}/${channel_directory}/latest.json"
  say "==> okf (newest ${channel} release)"
fi

say "    manifest: ${manifest_url}"
download "$manifest_url" "$tmp/latest.json" \
  || die "could not fetch ${manifest_url}
    If this hangs or cannot resolve, note that get.okf.tychostation.dev resolves only
    inside Ringo's network today — see ringo/okf-net#26."

resolved="$(manifest_version "$tmp/latest.json")"
[ -n "$resolved" ] || die "no version in ${manifest_url} — is it a release manifest?"

if [ -n "$version" ] && [ "$resolved" != "$version" ]; then
  die "asked for ${version} but ${manifest_url} describes ${resolved}"
fi

asset_path="$(manifest_asset "$tmp/latest.json" "$OKF_ASSET" path)"
asset_sha="$(manifest_asset "$tmp/latest.json" "$OKF_ASSET" sha256)"
[ -n "$asset_path" ] || die "the manifest for ${resolved} lists no ${OKF_ASSET} path"
[ -n "$asset_sha" ]  || die "the manifest for ${resolved} lists no ${OKF_ASSET} sha256"

asset_url="${OKF_BASE_URL}/${asset_path}"
dest="${OKF_DIR}/okf"

say "    version:  ${resolved}"
say "    binary:   ${asset_url}"
say "    sha256:   ${asset_sha}"
say "    install:  ${dest}"

if [ "$dry_run" -eq 1 ]; then
  say "==> --dry-run: nothing was downloaded or written"
  exit 0
fi

say "==> downloading ${OKF_ASSET}"
download "$asset_url" "$tmp/okf" || die "could not download ${asset_url}"

# Fail hard on a mismatch, and print both digests. `sha256sum -c` would report only
# "FAILED", and the two hashes are what tells a truncated download apart from the wrong
# file. Nothing has been written outside the temp directory at this point, so a mismatch
# leaves no partial install behind.
got="$(sha256_of "$tmp/okf")"
if [ "$got" != "$asset_sha" ]; then
  die "sha256 mismatch for ${OKF_ASSET}
    manifest:   ${asset_sha}
    downloaded: ${got}
    Refusing to install. Nothing was written to ${OKF_DIR}."
fi
say "    sha256 verified"

mkdir -p "$OKF_DIR" || die "could not create ${OKF_DIR}"
chmod 0755 "$tmp/okf"

# Install by rename, so `okf` is either the old binary or the new one and never a
# half-written file — which matters because the thing being replaced may be running.
# The staging copy is made inside OKF_DIR: rename is only atomic within a filesystem, and
# /tmp is very often a different one.
staging="${OKF_DIR}/.okf.install.$$"
cp "$tmp/okf" "$staging" || die "could not write to ${OKF_DIR}"
chmod 0755 "$staging"
mv -f "$staging" "$dest" || { rm -f "$staging"; die "could not install to ${dest}"; }

# macOS Gatekeeper (#36). curl tags anything it downloads with the com.apple.quarantine
# extended attribute, and Gatekeeper refuses to run a quarantined binary that is neither
# signed nor notarised: the user gets "cannot be opened because the developer cannot be
# verified", from a dialog, for a command-line tool they installed on purpose. okf is
# unsigned today — signing and notarisation need an Apple Developer account and are a
# post-1.0 problem — so the installer clears the flag it just caused.
#
# This is not a security bypass smuggled into an installer. The user has already piped
# this script into `sh`; removing an attribute from a file this same script downloaded,
# verified against the manifest's sha256 and wrote itself extends no further trust than
# that. What it does not do is weaken the check that matters: the digest was compared
# before anything was written, and a mismatch exits above without reaching here.
#
# Never fatal, and only when there is an `xattr` to run. The attribute may simply not be
# set — wget does not set it, nor does a copy from a local path — and `xattr -d` on an
# absent attribute is an error worth ignoring rather than an install worth failing.
# It runs BEFORE the version check below, because a quarantined binary would fail that.
if [ "$os" = Darwin ] && have xattr; then
  xattr -d com.apple.quarantine "$dest" >/dev/null 2>&1 || true
fi

say "==> installed"
if reported="$("$dest" version 2>/dev/null)"; then
  say "    $(basename "$dest") ${reported}"
else
  warn "install.sh: ${dest} was installed but did not answer \`okf version\`"
fi

# The agent skills (#41). They ship INSIDE the binary, so this is a set of file writes and
# not a second download: `okf skills install` writes okf's own copy under
# ~/.local/share/okf/skills, and additionally into Claude Code's and pi's skill directories
# when this machine already has them. It asks nothing — a prompt inside `curl … | sh` is a
# hang, and the interactive walkthrough is a separate issue.
#
# A failure here is a WARNING and never a failed install. The binary is downloaded,
# verified and in place by this point, and skills that did not land are one command away;
# exiting non-zero would tell a user their okf is broken when it is not.
#
# OKF_SKIP_SKILLS=1 opts out, for anyone who manages their agent's skill directories
# themselves and does not want an installer writing into them.
if [ "${OKF_SKIP_SKILLS:-0}" = 1 ]; then
  say "==> skills: skipped (OKF_SKIP_SKILLS=1)"
else
  say "==> installing the agent skills"
  if ! "$dest" skills install; then
    warn "install.sh: could not install the agent skills. okf itself is installed;
    run this when you want them:
        ${dest} skills install"
  fi
fi

# Shell completions (#51). The same shape as the skills step: the binary is installed and
# verified by now, and it prints its own completion script, so this is a file write and not
# a download.
#
# The shell is detected from $SHELL and from nothing else. That is a guess about the login
# shell rather than a fact about the one running this script — but the alternatives are
# worse: the shell running a `curl … | sh` pipeline is sh whatever the user's shell is, and
# asking a shell what it is means launching one, which an installer has no business doing.
# A shell this does not know gets nothing written and says nothing: a message about a shell
# somebody does not use is noise, and `okf completion --help` documents the manual path.
#
# A failure is a WARNING naming the command to run by hand, never a failed install — the
# same reading as the skills step. OKF_SKIP_COMPLETIONS=1 opts out.
if [ "${OKF_SKIP_COMPLETIONS:-0}" = 1 ]; then
  say "==> completions: skipped (OKF_SKIP_COMPLETIONS=1)"
else
  completion_shell="${SHELL:-}"
  completion_shell="${completion_shell##*/}"
  completion_file=""
  case "$completion_shell" in
    bash) completion_file="${XDG_DATA_HOME:-$HOME/.local/share}/bash-completion/completions/okf" ;;
    zsh)  completion_file="${XDG_DATA_HOME:-$HOME/.local/share}/zsh/site-functions/_okf" ;;
    fish) completion_file="${XDG_CONFIG_HOME:-$HOME/.config}/fish/completions/okf.fish" ;;
  esac

  if [ -n "$completion_file" ]; then
    say "==> installing the ${completion_shell} completion"
    completion_dir="${completion_file%/*}"
    completion_staging="${completion_file}.install.$$"

    # Written to a staging file beside the destination and renamed, so a completion a shell
    # is reading is never a half-written one.
    if mkdir -p "$completion_dir" \
      && "$dest" completion "$completion_shell" >"$completion_staging" \
      && mv -f "$completion_staging" "$completion_file"; then
      say "    ${completion_file}"

      # zsh only reads a directory that is on $fpath, and this script cannot see one shell's
      # $fpath from another shell. So the line is printed as a conditional for the reader to
      # apply rather than as a claim about their setup.
      if [ "$completion_shell" = zsh ]; then
        say "    if completions do not appear, put this in ~/.zshrc before compinit:"
        say "        fpath=(${completion_dir} \$fpath)"
      fi
    else
      rm -f "$completion_staging"
      warn "install.sh: could not write the ${completion_shell} completion to ${completion_file}.
    okf itself is installed; run this when you want it:
        ${dest} completion ${completion_shell} > ${completion_file}"
    fi
  fi
fi

# A PATH hint, and only when it is warranted. Compare against the real PATH entries rather
# than substring-matching, or /home/x/.local/binaries would count as a match — and compare
# them RESOLVED, because two spellings of one directory are both ordinary here: a PATH
# entry that carries a trailing slash, and an install dir reached through a symlink
# (`~/.local/bin` is very often a link into a dotfiles checkout). Printing an
# `export PATH=…` for a directory that is already on PATH is worse than printing nothing:
# the reader follows it, it does not help, and now they distrust the rest of the output.
#
# `cd -P && pwd -P` is the POSIX way to resolve one; `readlink -f` is GNU. A path that
# does not exist resolves to itself, which is the right answer for a PATH entry naming a
# directory that was never created.
physical() { # physical <dir>
  ( CDPATH=''; cd -P -- "$1" 2>/dev/null && pwd -P ) || printf '%s\n' "$1"
}

okf_dir_physical="$(physical "$OKF_DIR")"
on_path=0
saved_ifs="$IFS"
IFS=:
for entry in $PATH; do
  if [ "$(physical "$entry")" = "$okf_dir_physical" ]; then
    on_path=1
  fi
done
IFS="$saved_ifs"

if [ "$on_path" -eq 0 ]; then
  say ""
  say "    ${OKF_DIR} is not on your PATH. Add it:"
  say ""
  say "        export PATH=\"${OKF_DIR}:\$PATH\""
  say ""
  say "    (put that in ~/.profile, ~/.bashrc or ~/.zshrc to make it stick)"
fi

# Next time, there is a verb for this (#23). Said once, at the end, where a reader has
# already got what they came for: `okf upgrade` reads the same manifest this script just
# read, from the same OKF_INSTALL_URL, and replaces the binary in place — so nobody needs
# to pipe a script into a shell twice. It is the only okf command that touches a network,
# and only when it is run.
say ""
say "    later: \`okf upgrade --check\` says whether a newer release exists;"
say "           \`okf upgrade\` installs it, no re-download of this script."
