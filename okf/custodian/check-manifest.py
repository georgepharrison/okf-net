#!/usr/bin/env python3
"""Check the capture manifest's invariants for an OKF vault.

The manifest (`<vault>/raw/manifest.json`) is the immutability record for
everything the capture skill drops into `raw/`: it is the one place a raw
item's original URL survives, and its recorded `sha256` is what "unchanged"
is measured against. Nothing in `Okf.Core` reads it — `OkfLinter` walks a
*bundle root* and `raw/` sits outside every one of them — so the invariants
that hold the record together are checked here instead, by a script the CI
pipeline runs beside `okf lint`.

Deliberately read-only. A manifest that will not parse, or a hash that no
longer matches, is REPORTED and left exactly as found: a script that repairs
the immutability record is how the record is lost.

Python 3 standard library only (decisions.md §3: in-bundle and custodian
scripts are Python so they run in arbitrary consumer and CI environments).

Usage:
    check-manifest.py [vault-path]

`vault-path` defaults to this script's parent directory, which is the vault
root when the script sits at `<vault>/custodian/`.

Exit codes mirror `okf lint`: 0 clean, 1 violations found, 2 usage or
environment failure.
"""

from __future__ import annotations

import datetime
import hashlib
import json
import os
import re
import sys

MANIFEST_VERSION = 1

ID_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}-[a-z0-9]+(?:-[a-z0-9]+)*$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")

ENTRY_KEYS = ("id", "form", "files", "capturedAt", "capturedBy", "ingestion")

# Files in `raw/` that are vault machinery rather than captured evidence, and so are
# never expected to carry a manifest entry. `.gitignore` is `okf init`'s answer to a
# host repository whose own ignore patterns (`*.log`, `tmp/`) would otherwise silently
# eat a capture; it re-includes everything under `raw/` and records nothing.
VAULT_FILES = ("manifest.json", ".gitignore")


class Report:
    """Collected violations, each tied to the thing that broke."""

    def __init__(self) -> None:
        self.violations: list[str] = []
        self.checked_entries = 0
        self.checked_files = 0

    def fail(self, where: str, message: str) -> None:
        self.violations.append(f"{where}: {message}")


def sha256_of(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def is_iso_with_offset(value) -> bool:
    """True for `date -Iseconds` output — ISO 8601 carrying a UTC offset.

    The type guard is load-bearing: a missing or non-string timestamp is an
    ordinary hostile-manifest case, and `str.replace` on `None` would raise
    out of the report rather than into it.
    """
    if not isinstance(value, str):
        return False
    try:
        parsed = datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return parsed.tzinfo is not None


def is_calendar_date(value: str) -> bool:
    """True when `YYYY-MM-DD` names a day that exists — `2026-13-45` does not."""
    try:
        datetime.date.fromisoformat(value)
    except ValueError:
        return False
    return True


def raw_entries(raw_dir: str) -> tuple[set[str], set[str]]:
    """Every file under `raw/` and every symlink in it, relative to `raw/`.

    Symlinks are surfaced separately and never descended. `os.walk` does not
    walk into a symlinked directory, so an unrecorded tree would otherwise sit
    in `raw/` completely invisible to the "nothing here is unclaimed" check —
    and a link is not a retrieved artifact in any case: what a recorded sha256
    measures has to be the bytes that were captured, not a pointer that can be
    repointed.
    """
    files: set[str] = set()
    links: set[str] = set()

    def relative(path: str) -> str:
        return os.path.relpath(path, raw_dir).replace(os.sep, "/")

    for directory, dirnames, filenames in os.walk(raw_dir):
        for name in list(dirnames):
            if os.path.islink(os.path.join(directory, name)):
                links.add(relative(os.path.join(directory, name)))
                dirnames.remove(name)
        for name in filenames:
            full = os.path.join(directory, name)
            if os.path.islink(full):
                links.add(relative(full))
            elif relative(full) not in VAULT_FILES:
                files.add(relative(full))
    return files, links


def check_entry(entry, position: int, raw_dir: str, vault: str, seen_ids: dict,
                claimed: dict, report: Report) -> None:
    where = f"captures[{position}]"

    if not isinstance(entry, dict):
        report.fail(where, "entry is not a JSON object.")
        return

    entry_id = entry.get("id")
    if isinstance(entry_id, str) and entry_id:
        where = f"captures[{position}] ({entry_id})"

    for key in ENTRY_KEYS:
        if key not in entry:
            report.fail(where, f"required key `{key}` is missing.")

    # --- id: unique, and the dated slug the skill names ------------------
    if not isinstance(entry_id, str) or not ID_PATTERN.match(entry_id or ""):
        report.fail(where, "`id` must be `<YYYY-MM-DD>-<slug>` (lowercase).")
    else:
        # The pattern fixes the shape; only the calendar rejects `2026-13-45`,
        # and an id nobody can read as a date is not a capture date.
        if not is_calendar_date(entry_id[:10]):
            report.fail(where, f"`id` opens with `{entry_id[:10]}`, which is not a date.")
        if entry_id in seen_ids:
            report.fail(where, f"`id` duplicates captures[{seen_ids[entry_id]}].")
        else:
            seen_ids[entry_id] = position

    # --- form ------------------------------------------------------------
    form = entry.get("form")
    if form not in ("flat", "packet"):
        report.fail(where, "`form` must be `flat` or `packet`.")

    # --- capturedAt / capturedBy -----------------------------------------
    if not is_iso_with_offset(entry.get("capturedAt")):
        report.fail(where, "`capturedAt` must be ISO 8601 with an offset.")
    if not isinstance(entry.get("capturedBy"), str) or not entry.get("capturedBy"):
        report.fail(where, "`capturedBy` must be a non-empty actor string.")

    # --- files: they exist, they hash to what was recorded ---------------
    files = entry.get("files")
    if not isinstance(files, list) or not files:
        report.fail(where, "`files` must be a non-empty array.")
        files = []

    paths = []
    for index, item in enumerate(files):
        file_where = f"{where} files[{index}]"
        if not isinstance(item, dict):
            report.fail(file_where, "entry is not a JSON object.")
            continue

        path = item.get("path")
        recorded = item.get("sha256")

        if not isinstance(path, str) or not path:
            report.fail(file_where, "`path` must be a non-empty string.")
            continue
        if path.startswith("/") or ".." in path.split("/"):
            report.fail(file_where, f"`{path}` must stay inside raw/.")
            continue

        paths.append(path)
        if path in claimed:
            report.fail(file_where, f"`{path}` is already claimed by {claimed[path]}.")
        else:
            claimed[path] = where

        absolute = os.path.join(raw_dir, path)
        if not os.path.isfile(absolute):
            report.fail(file_where, f"`{path}` does not exist under raw/.")
            continue

        if not isinstance(recorded, str) or not SHA256_PATTERN.match(recorded):
            report.fail(file_where, "`sha256` must be 64 lowercase hex digits.")
            continue

        report.checked_files += 1
        actual = sha256_of(absolute)
        if actual != recorded:
            report.fail(
                file_where,
                f"`{path}` no longer matches its recorded sha256 "
                f"(recorded {recorded[:12]}…, on disk {actual[:12]}…). "
                "The artifact changed after capture, or the record did; "
                "resolve it by hand, never by rewriting the manifest.")

    # --- flat/packet layout ----------------------------------------------
    if form == "flat" and isinstance(entry_id, str):
        if len(paths) > 1:
            report.fail(where, "a `flat` capture holds exactly one file.")
        for path in paths:
            stem = path.rsplit(".", 1)[0] if "." in path else path
            if "/" in path or stem != entry_id:
                report.fail(where, f"a `flat` capture's file is `{entry_id}.<ext>`, not `{path}`.")
    if form == "packet" and isinstance(entry_id, str):
        for path in paths:
            if not path.startswith(f"{entry_id}/"):
                report.fail(where, f"a `packet` capture's files live under `{entry_id}/`, not `{path}`.")

    # --- ingestion: null while waiting, or naming concepts that exist ----
    ingestion = entry.get("ingestion")
    if ingestion is None:
        return
    if not isinstance(ingestion, dict):
        report.fail(where, "`ingestion` must be null or an object.")
        return

    if not is_iso_with_offset(ingestion.get("at")):
        report.fail(where, "`ingestion.at` must be ISO 8601 with an offset.")
    if not isinstance(ingestion.get("by"), str) or not ingestion.get("by"):
        report.fail(where, "`ingestion.by` must be a non-empty actor string.")

    concepts = ingestion.get("concepts")
    if not isinstance(concepts, list) or not concepts:
        report.fail(where, "`ingestion.concepts` must be a non-empty array.")
        return

    for index, concept in enumerate(concepts):
        concept_where = f"{where} ingestion.concepts[{index}]"
        if not isinstance(concept, str) or not concept:
            report.fail(concept_where, "must be a non-empty path.")
            continue
        # Vault-root-relative, NOT raw/-relative: one capture may be ingested
        # into more than one bundle.
        if concept.startswith("/") or ".." in concept.split("/"):
            report.fail(concept_where, f"`{concept}` must stay inside the vault.")
            continue
        if not os.path.isfile(os.path.join(vault, concept)):
            report.fail(
                concept_where,
                f"`{concept}` names no file under the vault root — an ingested "
                "capture must point at the concept that carries it.")


def main(argv: list[str]) -> int:
    if len(argv) > 2:
        print("usage: check-manifest.py [vault-path]", file=sys.stderr)
        return 2

    vault = os.path.abspath(
        argv[1] if len(argv) == 2
        else os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

    if not os.path.isdir(vault):
        print(f"check-manifest: `{vault}` is not a directory.", file=sys.stderr)
        return 2

    raw_dir = os.path.join(vault, "raw")
    manifest_path = os.path.join(raw_dir, "manifest.json")

    if not os.path.exists(manifest_path):
        # The skill's own reading: no manifest at all means nothing is waiting.
        # An empty `raw/` beside it is the ordinary state of a vault that has
        # captured nothing; a populated one is not.
        found, links = raw_entries(raw_dir) if os.path.isdir(raw_dir) else (set(), set())
        stray = found | links
        if stray:
            for path in sorted(stray):
                print(f"check-manifest: raw/{path}: present in raw/ with no manifest "
                      "to record where it came from.", file=sys.stderr)
            return 1
        print(f"check-manifest: no capture manifest at {os.path.relpath(manifest_path, vault)}; "
              "nothing captured. 0 violations.")
        return 0

    try:
        with open(manifest_path, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, json.JSONDecodeError) as exception:
        print(f"check-manifest: raw/manifest.json does not parse: {exception}",
              file=sys.stderr)
        print("check-manifest: left as found — repairing the immutability record "
              "is how the record is lost.", file=sys.stderr)
        return 1

    report = Report()

    if not isinstance(manifest, dict):
        report.fail("manifest", "top level must be a JSON object.")
        manifest = {}
    if manifest.get("manifestVersion") != MANIFEST_VERSION:
        report.fail("manifest", f"`manifestVersion` must be {MANIFEST_VERSION}.")

    captures = manifest.get("captures")
    if not isinstance(captures, list):
        report.fail("manifest", "`captures` must be an array.")
        captures = []

    seen_ids: dict = {}
    claimed: dict = {}
    for position, entry in enumerate(captures):
        report.checked_entries += 1
        check_entry(entry, position, raw_dir, vault, seen_ids, claimed, report)

    # Nothing sits in raw/ unrecorded: an artifact with no manifest entry has
    # no original URL, no hash, and no place in the custodian's work queue.
    found, links = raw_entries(raw_dir)
    for path in sorted(found - set(claimed)):
        report.fail(f"raw/{path}", "no manifest entry claims this file.")
    for path in sorted(links):
        report.fail(
            f"raw/{path}",
            "is a symbolic link, not a captured artifact. raw/ holds the bytes "
            "that were retrieved; a link points at bytes that can be repointed, "
            "and a linked directory is a tree this check cannot walk.")

    for violation in report.violations:
        print(f"check-manifest: {violation}", file=sys.stderr)

    waiting = sum(
        1 for entry in captures
        if isinstance(entry, dict) and entry.get("ingestion") is None)
    print(f"Checked {report.checked_entries} captures "
          f"({report.checked_files} files hashed, {waiting} awaiting ingestion): "
          f"{len(report.violations)} violations.")

    return 1 if report.violations else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
