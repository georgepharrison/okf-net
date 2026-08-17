#!/usr/bin/env python3
"""Stage public release manifests and installers beside the generated Pages site.

Only latest.json, install.sh, and install.ps1 are copied. Binaries and archives
remain GitHub Release assets, never Pages assets.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path
from urllib.parse import quote

from publish_github_release import ApiError, GitHubApi, is_public_download_url, repository_path


STABLE_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")
RC_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)-rc\.(\d+)$")
SMALL_ASSETS = ("latest.json", "install.sh", "install.ps1")


def version_key(tag: str) -> tuple[int, ...] | None:
    stable = STABLE_TAG.fullmatch(tag)
    if stable:
        return tuple(int(value) for value in stable.groups()) + (1, 0)
    rc = RC_TAG.fullmatch(tag)
    if rc:
        return tuple(int(value) for value in rc.groups()[:3]) + (0, int(rc.group(4)))
    return None


def public_asset_url(repository: str, tag: str, name: str) -> str:
    return f"https://github.com/{repository}/releases/download/{quote(tag, safe='')}/{quote(name, safe='')}"


def listed_assets(release: dict) -> dict[str, dict]:
    return {asset.get("name"): asset for asset in release.get("assets", [])}


def fetch_small_assets(api: GitHubApi, repository: str, release: dict) -> dict[str, bytes]:
    tag = release.get("tag_name")
    assets = listed_assets(release)
    missing = [name for name in SMALL_ASSETS if name not in assets]
    if missing:
        raise RuntimeError(f"GitHub release {tag} is missing Pages assets: {missing}")

    data: dict[str, bytes] = {}
    for name in SMALL_ASSETS:
        asset_url = assets[name].get("url")
        if not isinstance(asset_url, str) or not asset_url.startswith("https://api.github.com/"):
            raise RuntimeError(f"GitHub release {tag} has no safe API URL for {name}")
        data[name] = api.download(asset_url)

    try:
        manifest = json.loads(data["latest.json"])
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RuntimeError(f"GitHub release {tag} has an invalid latest.json: {error}") from error
    if manifest.get("tag") != tag or manifest.get("version") != tag.removeprefix("v"):
        raise RuntimeError(f"latest.json for {tag} does not describe that release")

    manifest_assets = manifest.get("assets")
    if not isinstance(manifest_assets, dict):
        raise RuntimeError(f"latest.json for {tag} has no assets object")
    for name in ("install.sh", "install.ps1"):
        entry = manifest_assets.get(name)
        if not isinstance(entry, dict):
            raise RuntimeError(f"latest.json for {tag} has no {name} entry")
        digest = entry.get("sha256")
        if not isinstance(digest, str) or hashlib.sha256(data[name]).hexdigest() != digest:
            raise RuntimeError(f"{name} from GitHub release {tag} fails its manifest digest")
        expected_url = public_asset_url(repository, tag, name)
        download_url = entry.get("downloadUrl")
        if download_url != expected_url or not is_public_download_url(download_url):
            raise RuntimeError(
                f"latest.json for {tag} must carry validated downloadUrl {expected_url} for {name}"
            )
    return data


def all_published_releases(api: GitHubApi, repository: str) -> list[dict]:
    releases: list[dict] = []
    page = 1
    while True:
        batch = api.request(
            "GET",
            repository_path(repository, f"/releases?per_page=100&page={page}"),
        )
        if not batch:
            return releases
        for release in batch:
            if not release.get("draft") and version_key(release.get("tag_name", "")) is not None:
                releases.append(release)
        if len(batch) < 100:
            return releases
        page += 1


def clean_release_paths(out: Path) -> None:
    for name in ("stable", "dev"):
        path = out / name
        if path.is_dir() or path.is_symlink():
            shutil.rmtree(path) if path.is_dir() and not path.is_symlink() else path.unlink()
        elif path.exists():
            path.unlink()
    for path in out.iterdir():
        if path.is_dir() and version_key(path.name) is not None:
            shutil.rmtree(path)


def write_release(out: Path, tag: str, data: dict[str, bytes]) -> None:
    directory = out / tag
    directory.mkdir(parents=True)
    for name in SMALL_ASSETS:
        (directory / name).write_bytes(data[name])
    actual = sorted(path.name for path in directory.iterdir())
    if actual != sorted(SMALL_ASSETS):
        raise RuntimeError(f"Pages release directory {directory} contains unexpected files: {actual}")


def prepare(api: GitHubApi, repository: str, out: Path) -> None:
    releases = all_published_releases(api, repository)
    out.mkdir(parents=True, exist_ok=True)
    clean_release_paths(out)
    if not releases:
        print("warning: GitHub has no published semver releases; Pages site staged without install files")
        return
    releases.sort(key=lambda release: version_key(release["tag_name"]))
    stable = [release for release in releases if STABLE_TAG.fullmatch(release["tag_name"])]
    selected_stable = max(stable, key=lambda release: version_key(release["tag_name"])) if stable else None
    selected_rc = max(
        (release for release in releases if RC_TAG.fullmatch(release["tag_name"])),
        key=lambda release: version_key(release["tag_name"]),
        default=None,
    )

    by_tag = {release["tag_name"]: release for release in releases}
    for tag in sorted(by_tag, key=version_key):
        write_release(out, tag, fetch_small_assets(api, repository, by_tag[tag]))

    stable_tag = selected_stable["tag_name"] if selected_stable is not None else None
    if stable_tag is not None:
        shutil.copytree(out / stable_tag, out / "stable")
        for name in SMALL_ASSETS:
            shutil.copyfile(out / "stable" / name, out / name)
    else:
        print("warning: no published stable release; stable root aliases are not staged", file=sys.stderr)
    if selected_rc is not None:
        shutil.copytree(out / selected_rc["tag_name"], out / "dev")
    else:
        print("warning: no published release candidate; dev channel not staged", file=sys.stderr)

    print(f"==> Pages release channels: stable={stable_tag or 'none'}, rc={selected_rc['tag_name'] if selected_rc else 'none'}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--token", default=None)
    args = parser.parse_args(argv)
    import os

    try:
        api = GitHubApi(args.token or os.environ.get("GITHUB_TOKEN", ""))
        prepare(api, args.repository, args.out)
    except (ApiError, OSError, RuntimeError, ValueError) as error:
        print(f"Pages release staging failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
