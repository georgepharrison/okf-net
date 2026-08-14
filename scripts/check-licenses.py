#!/usr/bin/env python3
"""Check every dependency in a CycloneDX SBOM against an SPDX allowlist.

GitLab CE has no license-compliance policies (that is an Ultimate feature), so
AGENTS.md's "dependencies must be Apache-2.0-compatible" rule is enforced here
instead: `dotnet CycloneDX` writes an SBOM carrying each package's license
metadata, and this script fails the build on anything the allowlist does not
cover.

Policy (deliberately conservative -- when in doubt, fail):

  * A component whose license is a plain SPDX id passes iff that id is on the
    allowlist.
  * A component whose license is an SPDX *expression* passes iff the expression
    is wholly satisfiable from the allowlist: `OR` needs one side allowed,
    `AND` and `WITH` need every part allowed. A `WITH` exception identifier is
    a part like any other, so it too must be allowed -- which it never is,
    because exceptions are not licenses. That is intentional: an expression
    like `GPL-2.0-only WITH Universal-FOSS-Exception-1.0` must not slip through
    on the strength of an exception nobody reviewed.
  * A component listing *several* licenses must have all of them allowed.
    CycloneDX does not say whether a multi-entry list is a choice or a
    conjunction, so it is read as a conjunction -- the failing-safe reading.
  * A component with no SPDX id at all (license declared as a file or a bare
    URL, or no metadata) fails unless scripts/license-overrides.json pins its
    exact name + version to a verified, allowed license with evidence.
  * Anything not understood -- `+` (or-later) suffixes, `LicenseRef-*`,
    lower-case operators, malformed expressions -- fails.

Stdlib only, so it runs on any python3 (the CI image, a bare runner, a laptop)
without a virtualenv.

Usage:
    scripts/check-licenses.py [--sbom PATH] [--allowlist PATH] [--overrides PATH]

Exit status: 0 if every component resolves to an allowed license, 1 otherwise
(2 for a usage/IO problem).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DEFAULT_SBOM = os.path.join(REPO_ROOT, "artifacts", "sbom", "bom.json")
DEFAULT_ALLOWLIST = os.path.join(REPO_ROOT, "scripts", "licenses-allowed.json")
DEFAULT_OVERRIDES = os.path.join(REPO_ROOT, "scripts", "license-overrides.json")

# Our own projects, when the SBOM includes them (e.g. --include-project-references).
OWN_COMPONENT = re.compile(r"^Okf(\.|$)")

# A bare SPDX identifier. Anything else in an operand position -- `MIT+`,
# `LicenseRef-Foo`, a stray quote -- is deliberately not matched and so fails.
SPDX_ID = re.compile(r"^[A-Za-z0-9.\-]+$")

OPERATORS = ("AND", "OR", "WITH")


class ExpressionError(ValueError):
    """The SPDX expression could not be parsed, so it cannot be trusted."""


# --------------------------------------------------------------------------
# SPDX expression evaluation
# --------------------------------------------------------------------------


def tokenize(expression: str) -> list[str]:
    """Split an SPDX expression into identifiers, operators and parentheses."""
    tokens: list[str] = []
    for chunk in expression.replace("(", " ( ").replace(")", " ) ").split():
        tokens.append(chunk)
    if not tokens:
        raise ExpressionError("empty expression")
    return tokens


class _Parser:
    """Recursive-descent parser for the SPDX expression grammar.

    Precedence, tightest first: WITH, AND, OR (SPDX Annex D).
    """

    def __init__(self, tokens: list[str]) -> None:
        self._tokens = tokens
        self._pos = 0

    def parse(self) -> "Node":
        node = self._parse_or()
        if self._pos != len(self._tokens):
            raise ExpressionError(f"unexpected token {self._tokens[self._pos]!r}")
        return node

    def _peek(self) -> str | None:
        return self._tokens[self._pos] if self._pos < len(self._tokens) else None

    def _next(self) -> str:
        if self._pos >= len(self._tokens):
            raise ExpressionError("unexpected end of expression")
        token = self._tokens[self._pos]
        self._pos += 1
        return token

    def _parse_or(self) -> "Node":
        node = self._parse_and()
        while self._peek() == "OR":
            self._next()
            node = Node("OR", [node, self._parse_and()])
        return node

    def _parse_and(self) -> "Node":
        node = self._parse_with()
        while self._peek() == "AND":
            self._next()
            node = Node("AND", [node, self._parse_with()])
        return node

    def _parse_with(self) -> "Node":
        node = self._parse_atom()
        while self._peek() == "WITH":
            self._next()
            # An exception is required to be allowed in its own right; see the
            # module docstring for why.
            node = Node("AND", [node, self._parse_atom()])
        return node

    def _parse_atom(self) -> "Node":
        token = self._next()
        if token == "(":
            node = self._parse_or()
            if self._next() != ")":
                raise ExpressionError("unbalanced parentheses")
            return node
        if token == ")":
            raise ExpressionError("unbalanced parentheses")
        if token in OPERATORS:
            raise ExpressionError(f"operator {token!r} where a license id was expected")
        if not SPDX_ID.match(token):
            raise ExpressionError(f"{token!r} is not a plain SPDX identifier")
        return Node("ID", [], token)


class Node:
    __slots__ = ("kind", "children", "identifier")

    def __init__(self, kind: str, children: list["Node"], identifier: str = "") -> None:
        self.kind = kind
        self.children = children
        self.identifier = identifier

    def evaluate(self, allowed: set[str], disallowed: set[str]) -> bool:
        """Return whether this node is satisfiable; collect blocking ids."""
        if self.kind == "ID":
            if self.identifier.lower() in allowed:
                return True
            disallowed.add(self.identifier)
            return False
        results = [child.evaluate(allowed, disallowed) for child in self.children]
        if self.kind == "OR":
            return any(results)
        return all(results)


def evaluate_expression(expression: str, allowed: set[str]) -> tuple[bool, list[str]]:
    """Evaluate an SPDX expression against the allowlist.

    Returns (satisfied, blocking identifiers). Raises ExpressionError when the
    expression cannot be parsed.
    """
    node = _Parser(tokenize(expression)).parse()
    blocking: set[str] = set()
    satisfied = node.evaluate(allowed, blocking)
    return satisfied, sorted(blocking)


# --------------------------------------------------------------------------
# SBOM reading
# --------------------------------------------------------------------------


def declared_licenses(component: dict) -> tuple[list[str], list[str]]:
    """Split a component's `licenses` array into SPDX texts and un-checkable notes.

    CycloneDX spells a license either as `{"license": {"id": ...}}`,
    `{"expression": ...}`, or -- when NuGet only had a license *file* or a bare
    `licenseUrl` -- as `{"license": {"name": ..., "url": ...}}`, which carries no
    SPDX id and therefore cannot be checked. Some producers also stuff a whole
    expression into `id`; that is detected here rather than trusted as an id.
    """
    spdx: list[str] = []
    unchecked: list[str] = []
    for entry in component.get("licenses") or []:
        if not isinstance(entry, dict):
            unchecked.append(repr(entry))
            continue
        if "expression" in entry:
            spdx.append(str(entry["expression"]).strip())
            continue
        license_obj = entry.get("license")
        if not isinstance(license_obj, dict):
            unchecked.append(repr(entry))
            continue
        identifier = license_obj.get("id")
        if identifier:
            spdx.append(str(identifier).strip())
            continue
        name = license_obj.get("name")
        url = license_obj.get("url")
        unchecked.append(
            "name={!r}{}".format(name, f" url={url!r}" if url else "")
            if name
            else (f"url={url!r}" if url else repr(license_obj))
        )
    return spdx, unchecked


def load_json(path: str, what: str) -> dict:
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except FileNotFoundError:
        sys.exit(f"error: {what} not found: {path}")
    except json.JSONDecodeError as exc:
        sys.exit(f"error: {what} is not valid JSON ({path}): {exc}")


def load_allowlist(path: str) -> set[str]:
    allowed = load_json(path, "allowlist").get("allowed")
    if not isinstance(allowed, dict) or not allowed:
        sys.exit(f"error: allowlist {path} has no non-empty `allowed` object")
    return {identifier.lower() for identifier in allowed}


def load_overrides(path: str, allowed: set[str]) -> dict[tuple[str, str], dict]:
    """Index overrides by (lowercased name, version), rejecting unusable entries."""
    raw = load_json(path, "overrides file").get("overrides")
    if raw is None:
        raw = []
    if not isinstance(raw, list):
        sys.exit(f"error: overrides file {path} has a non-list `overrides`")

    overrides: dict[tuple[str, str], dict] = {}
    problems: list[str] = []
    for index, entry in enumerate(raw):
        if not isinstance(entry, dict):
            problems.append(f"overrides[{index}] is not an object")
            continue
        missing = [
            field
            for field in ("name", "version", "license", "evidence")
            if not str(entry.get(field, "")).strip()
        ]
        if missing:
            problems.append(
                f"overrides[{index}] ({entry.get('name', '?')}) is missing: "
                + ", ".join(missing)
            )
            continue
        # An override records what a package *is*; it is not an exemption, so
        # the license it substitutes must still be on the allowlist.
        if str(entry["license"]).lower() not in allowed:
            problems.append(
                f"overrides[{index}] ({entry['name']}@{entry['version']}) substitutes "
                f"{entry['license']!r}, which is not on the allowlist"
            )
            continue
        overrides[(str(entry["name"]).lower(), str(entry["version"]))] = entry

    if problems:
        print(f"error: unusable entries in {path}:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        sys.exit(2)
    return overrides


# --------------------------------------------------------------------------
# Reporting
# --------------------------------------------------------------------------


class Failure:
    def __init__(self, name: str, version: str, declared: str, reason: str) -> None:
        self.name = name
        self.version = version
        self.declared = declared
        self.reason = reason

    def render(self) -> str:
        return (
            f"FAIL  {self.name} {self.version}\n"
            f"        declared: {self.declared}\n"
            f"        reason:   {self.reason}"
        )


def check(
    sbom: dict,
    allowed: set[str],
    overrides: dict[tuple[str, str], dict],
    overrides_path: str,
) -> tuple[list[Failure], list[str], set[tuple[str, str]]]:
    failures: list[Failure] = []
    passes: list[str] = []
    used_overrides: set[tuple[str, str]] = set()

    for component in sbom.get("components") or []:
        name = str(component.get("name", "<unnamed>"))
        version = str(component.get("version", ""))
        if OWN_COMPONENT.match(name):
            continue

        spdx, unchecked = declared_licenses(component)
        key = (name.lower(), version)

        if not spdx:
            override = overrides.get(key)
            if override:
                used_overrides.add(key)
                declared = ", ".join(unchecked) if unchecked else "no license metadata"
                passes.append(
                    f"OK    {name} {version}  {override['license']} "
                    f"(override; SBOM had {declared})"
                )
                continue
            failures.append(
                Failure(
                    name,
                    version,
                    ", ".join(unchecked) if unchecked else "no license metadata",
                    "the SBOM carries no SPDX license id for this component "
                    "(license declared as a file or bare URL, or absent). Verify the "
                    f"license and record it in {os.path.relpath(overrides_path, REPO_ROOT)} "
                    "with an evidence link, or drop the package.",
                )
            )
            continue

        blocking: list[str] = []
        parse_errors: list[str] = []
        satisfied = True
        for expression in spdx:
            try:
                ok, blockers = evaluate_expression(expression, allowed)
            except ExpressionError as exc:
                satisfied = False
                parse_errors.append(f"{expression!r}: {exc}")
                continue
            if not ok:
                satisfied = False
                blocking.extend(blockers)

        declared = ", ".join(spdx)
        if unchecked:
            declared += " (+ un-checkable: " + ", ".join(unchecked) + ")"

        if satisfied and not unchecked:
            passes.append(f"OK    {name} {version}  {declared}")
            continue

        if parse_errors:
            reason = "could not parse the SPDX expression, so it cannot be trusted: " + (
                "; ".join(parse_errors)
            )
        elif blocking:
            reason = (
                "not satisfiable from the allowlist; blocked by: "
                + ", ".join(sorted(set(blocking)))
            )
        else:
            reason = (
                "carries a license entry with no SPDX id alongside its SPDX ones, "
                "so the full licensing is not established"
            )
        failures.append(Failure(name, version, declared, reason))

    return failures, passes, used_overrides


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Check CycloneDX SBOM component licenses against an SPDX allowlist."
    )
    parser.add_argument("--sbom", default=DEFAULT_SBOM, help="CycloneDX JSON SBOM")
    parser.add_argument("--allowlist", default=DEFAULT_ALLOWLIST)
    parser.add_argument("--overrides", default=DEFAULT_OVERRIDES)
    parser.add_argument(
        "-q", "--quiet", action="store_true", help="print failures only"
    )
    args = parser.parse_args(argv)

    allowed = load_allowlist(args.allowlist)
    overrides = load_overrides(args.overrides, allowed)
    sbom = load_json(args.sbom, "SBOM")

    failures, passes, used_overrides = check(sbom, allowed, overrides, args.overrides)

    if not args.quiet:
        for line in passes:
            print(line)

    stale = sorted(set(overrides) - used_overrides)
    for name, version in stale:
        print(
            f"warning: unused override for {name} {version} in {args.overrides} "
            "(package gone or no longer missing its SPDX id) -- remove it",
            file=sys.stderr,
        )

    checked = len(passes) + len(failures)
    if failures:
        print("", file=sys.stderr)
        for failure in failures:
            print(failure.render(), file=sys.stderr)
        print("", file=sys.stderr)
        print(
            f"{len(failures)} of {checked} components have a license that is not "
            f"allowed by {os.path.relpath(args.allowlist, REPO_ROOT)}. "
            "See AGENTS.md: dependencies must be Apache-2.0-compatible.",
            file=sys.stderr,
        )
        return 1

    print(f"\nAll {checked} dependency components have an allowed license.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
