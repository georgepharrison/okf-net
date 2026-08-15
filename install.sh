#!/bin/sh
# okf installer.
#
#   curl -fsSL https://get.tychostation.dev/install.sh | sh
#
# Downloads the newest `okf` release, verifies it against the release manifest, and
# installs it to ~/.local/bin/okf. Re-running is safe: it reinstalls the same version over
# itself rather than accumulating anything.
#
# This file is served BY a release as well as living in the repository: the tag pipeline
# uploads it alongside the binary, so the installer a release hands you is the one that
# release was built with. See docs/decisions.md.
#
# Usage:
#   install.sh                      install the newest release
#   install.sh --version 1.0.0-rc.15
#   install.sh --dry-run            say what would happen, write nothing
#   install.sh --help
#
# Environment:
#   OKF_INSTALL_URL   base URL to install from   (default https://get.tychostation.dev)
#   OKF_INSTALL_DIR   directory to install into  (default $HOME/.local/bin)
#
# POSIX sh on purpose — no bashisms. The one thing an installer may not assume is a shell,
# and `curl | sh` on a Debian box runs dash.

set -eu

OKF_BASE_URL="${OKF_INSTALL_URL:-https://get.tychostation.dev}"
OKF_DIR="${OKF_INSTALL_DIR:-$HOME/.local/bin}"
OKF_ASSET="okf-linux-x64"

version=""
dry_run=0

usage() {
  cat <<'EOF'
usage: install.sh [--version <version>] [--dry-run]

  --version <v>   install this release instead of the newest (e.g. 1.0.0-rc.15,
                  with or without a leading `v`)
  --dry-run       report what would be downloaded and installed; write nothing
  -h, --help      this message

environment:
  OKF_INSTALL_URL   base URL to install from  (default https://get.tychostation.dev)
  OKF_INSTALL_DIR   install directory         (default $HOME/.local/bin)
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
    --dry-run) dry_run=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) warn "install.sh: unknown argument: $1"; usage >&2; exit 2 ;;
  esac
done

# ---------------------------------------------------------------------------
# Platform
#
# Refuse early and by name. An installer that downloads a linux-x64 binary onto an arm64
# Mac and then fails with "cannot execute binary file" has told the user nothing about why.
# ---------------------------------------------------------------------------

os="$(uname -s 2>/dev/null || echo unknown)"
arch="$(uname -m 2>/dev/null || echo unknown)"

case "$os" in
  Linux) ;;
  *)
    die "okf ships a linux-x86_64 binary only; this is ${os}.
    Build from source instead: https://gitlab.tychostation.dev/ringo/okf-net"
    ;;
esac

case "$arch" in
  x86_64|amd64) ;;
  *)
    die "okf ships a linux-x86_64 binary only; this machine is ${arch}.
    There is no ${arch} build yet. Build from source instead:
    https://gitlab.tychostation.dev/ringo/okf-net"
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

download() { # download <url> <dest>
  case "$downloader" in
    curl) curl --fail --silent --show-error --location --output "$2" "$1" ;;
    wget) wget --quiet --output-document "$2" "$1" ;;
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
  manifest_url="${OKF_BASE_URL}/latest.json"
  say "==> okf (newest release)"
fi

say "    manifest: ${manifest_url}"
download "$manifest_url" "$tmp/latest.json" \
  || die "could not fetch ${manifest_url}
    If this hangs or cannot resolve, note that get.tychostation.dev resolves only
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

say "==> installed"
if reported="$("$dest" version 2>/dev/null)"; then
  say "    $(basename "$dest") ${reported}"
else
  warn "install.sh: ${dest} was installed but did not answer \`okf version\`"
fi

# A PATH hint, and only when it is warranted. Compare against the real PATH entries rather
# than substring-matching, or /home/x/.local/binaries would count as a match.
on_path=0
saved_ifs="$IFS"
IFS=:
for entry in $PATH; do
  if [ "$entry" = "$OKF_DIR" ]; then
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
