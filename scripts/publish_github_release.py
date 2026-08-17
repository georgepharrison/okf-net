#!/usr/bin/env python3
"""Publish the eight verified GitLab release assets to a GitHub release.

The GitLab tag job owns the bytes. This helper only verifies the mirrored tag,
creates or reuses a draft release, uploads missing assets, verifies every asset
by downloading it back, publishes the release, and asks Pages to refresh.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


STABLE_TAG = re.compile(r"^v\d+\.\d+\.\d+$")
EXPECTED_ASSETS = (
    "okf-linux-x64",
    "okf-osx-arm64",
    "okf-win-x64.exe",
    "okf-net-knowledge.tar.gz",
    "okf-skills.tar.gz",
    "latest.json",
    "install.sh",
    "install.ps1",
)
RC_TAG = re.compile(r"^v\d+\.\d+\.\d+-rc\.\d+$")


def is_release_candidate(tag: str) -> bool:
    if RC_TAG.fullmatch(tag):
        return True
    if STABLE_TAG.fullmatch(tag):
        return False
    raise RuntimeError(f"tag {tag!r} is neither a stable semver tag nor a strict -rc.N tag")


class ApiError(RuntimeError):
    def __init__(self, status: int, message: str, body: Any = None):
        super().__init__(f"GitHub API HTTP {status}: {message}")
        self.status = status
        self.body = body


class GitHubApi:
    def __init__(self, token: str, api_base: str = "https://api.github.com"):
        if not token:
            raise ValueError("OKF_GITHUB_TOKEN is required")
        self.api_base = api_base.rstrip("/")
        self.token = token

    def request(self, method: str, path: str, body: Any = None, accept: str = "application/vnd.github+json") -> Any:
        payload = None if body is None else json.dumps(body).encode("utf-8")
        request = Request(
            self.api_base + path,
            data=payload,
            method=method,
            headers={
                "Accept": accept,
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/json",
                "User-Agent": "okf-net-release-publisher",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        try:
            with urlopen(request, timeout=60) as response:
                data = response.read()
                if not data:
                    return None
                return json.loads(data)
        except HTTPError as error:
            raw = error.read().decode("utf-8", "replace")
            try:
                parsed = json.loads(raw)
            except json.JSONDecodeError:
                parsed = raw
            message = parsed.get("message", raw) if isinstance(parsed, dict) else raw
            raise ApiError(error.code, str(message), parsed) from error
        except URLError as error:
            raise RuntimeError(f"GitHub API request failed: {error.reason}") from error

    def upload(self, url: str, data: bytes) -> None:
        request = Request(
            url,
            data=data,
            method="POST",
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/octet-stream",
                "User-Agent": "okf-net-release-publisher",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        try:
            with urlopen(request, timeout=180) as response:
                response.read()
        except (HTTPError, URLError) as error:
            raise RuntimeError(f"could not upload {url}: {error}") from error

    def download(self, url: str) -> bytes:
        request = Request(
            url,
            headers={
                "Accept": "application/octet-stream",
                "Authorization": f"Bearer {self.token}",
                "User-Agent": "okf-net-release-publisher",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        try:
            with urlopen(request, timeout=120) as response:
                return response.read()
        except (HTTPError, URLError) as error:
            status = getattr(error, "code", 0)
            raise ApiError(status, f"could not download {url}: {error}") from error


@dataclass(frozen=True)
class LocalAsset:
    name: str
    path: Path


def repository_path(repository: str, suffix: str) -> str:
    return f"/repos/{repository}{suffix}"


def resolve_tag(api: GitHubApi, repository: str, tag: str) -> str:
    """Resolve a lightweight or annotated Git tag to its commit SHA."""
    reference = api.request(
        "GET",
        repository_path(repository, f"/git/ref/tags/{quote(tag, safe='')}"),
    )
    obj = reference.get("object", {})
    sha = obj.get("sha")
    kind = obj.get("type")
    if not sha:
        raise RuntimeError(f"GitHub tag {tag} has no target SHA")
    for _ in range(4):
        if kind == "commit":
            return sha
        if kind != "tag":
            raise RuntimeError(f"GitHub tag {tag} resolves to unsupported object type {kind!r}")
        annotated = api.request("GET", repository_path(repository, f"/git/tags/{sha}"))
        target = annotated.get("object", {})
        sha = target.get("sha")
        kind = target.get("type")
        if not sha:
            break
    raise RuntimeError(f"GitHub tag {tag} has too many or invalid annotated-tag indirections")


def wait_for_mirrored_tag(
    api: GitHubApi,
    repository: str,
    tag: str,
    commit_sha: str,
    attempts: int = 40,
    interval: float = 15.0,
    sleep: Callable[[float], None] = time.sleep,
) -> None:
    last_error: Exception | None = None
    for attempt in range(1, attempts + 1):
        try:
            resolved = resolve_tag(api, repository, tag)
            if resolved != commit_sha:
                raise RuntimeError(
                    f"GitHub tag {tag} resolves to {resolved}, not CI_COMMIT_SHA {commit_sha}; refusing to publish"
                )
            print(f"==> mirrored tag {tag} resolves to {commit_sha}")
            return
        except ApiError as error:
            if error.status != 404:
                raise
            last_error = error
        except RuntimeError as error:
            # A tag pointing at a different commit is a permanent safety failure, not a
            # mirror race; do not retry it or accidentally publish under the wrong tag.
            if "not CI_COMMIT_SHA" in str(error):
                raise
            last_error = error
        if attempt != attempts:
            print(f"    mirrored tag not available (attempt {attempt}/{attempts}); waiting")
            sleep(interval)
    raise RuntimeError(f"GitHub tag {tag} did not appear after {attempts} attempts: {last_error}")


def read_local_assets(paths: list[LocalAsset], tag: str) -> tuple[dict[str, LocalAsset], dict[str, Any]]:
    by_name = {asset.name: asset for asset in paths}
    missing = [name for name in EXPECTED_ASSETS if name not in by_name]
    extra = [name for name in by_name if name not in EXPECTED_ASSETS]
    if missing or extra:
        raise RuntimeError(f"release must contain exactly eight assets; missing={missing}, extra={extra}")

    manifest_path = by_name["latest.json"].path
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(f"cannot read release manifest {manifest_path}: {error}") from error
    if manifest.get("tag") != tag or manifest.get("version") != tag.removeprefix("v"):
        raise RuntimeError(f"latest.json does not describe tag {tag}")
    assets = manifest.get("assets")
    if not isinstance(assets, dict):
        raise RuntimeError("latest.json has no assets object")

    for name in EXPECTED_ASSETS:
        if name == "latest.json":
            continue
        entry = assets.get(name)
        if not isinstance(entry, dict) or not isinstance(entry.get("sha256"), str):
            raise RuntimeError(f"latest.json has no digest for {name}")
        digest = hashlib.sha256(by_name[name].path.read_bytes()).hexdigest()
        if digest != entry["sha256"]:
            raise RuntimeError(f"local {name} does not match latest.json: {digest} != {entry['sha256']}")
        if entry.get("size") != by_name[name].path.stat().st_size:
            raise RuntimeError(f"local {name} does not match latest.json size")
        public_url = entry.get("downloadUrl")
        if not isinstance(public_url, str) or not is_public_download_url(public_url):
            raise RuntimeError(f"latest.json has no validated HTTPS downloadUrl for {name}")
    return by_name, manifest


def is_public_download_url(url: str) -> bool:
    from urllib.parse import urlparse

    parsed = urlparse(url)
    return (
        parsed.scheme == "https"
        and bool(parsed.netloc)
        and not parsed.username
        and not parsed.password
        and not parsed.fragment
    )


def release_for_tag(api: GitHubApi, repository: str, tag: str, commit_sha: str) -> dict[str, Any]:
    path = repository_path(repository, f"/releases/tags/{quote(tag, safe='')}")
    try:
        release = api.request("GET", path)
    except ApiError as error:
        if error.status != 404:
            raise
        print(f"==> creating draft GitHub release for {tag}")
        return api.request(
            "POST",
            repository_path(repository, "/releases"),
            {
                "tag_name": tag,
                "target_commitish": commit_sha,
                "name": tag,
                "draft": True,
                "prerelease": is_release_candidate(tag),
            },
        )
    if release.get("tag_name") != tag:
        raise RuntimeError(f"GitHub returned release for unexpected tag {release.get('tag_name')!r}")
    return release


def verify_release_assets(
    api: GitHubApi,
    release: dict[str, Any],
    local: dict[str, LocalAsset],
) -> None:
    remote = {asset.get("name"): asset for asset in release.get("assets", [])}
    unexpected = sorted(set(remote) - set(EXPECTED_ASSETS))
    if unexpected:
        raise RuntimeError(f"GitHub release has unexpected assets: {unexpected}")
    for name in EXPECTED_ASSETS:
        if name not in remote:
            continue
        data = api.download(remote[name]["url"])
        expected = local[name].path.read_bytes()
        if data != expected:
            raise RuntimeError(f"GitHub release asset {name} differs; refusing to replace it")
        print(f"    verified existing {name} ({len(data)} bytes)")


def upload_missing_assets(api: GitHubApi, release: dict[str, Any], local: dict[str, LocalAsset]) -> None:
    remote = {asset.get("name") for asset in release.get("assets", [])}
    upload_url = release.get("upload_url", "").split("{", 1)[0]
    if not upload_url:
        raise RuntimeError("GitHub release has no upload URL")
    for name in EXPECTED_ASSETS:
        if name in remote:
            continue
        path = local[name].path
        url = upload_url + "?name=" + quote(name, safe="")
        print(f"==> uploading {name} ({path.stat().st_size} bytes)")
        # Upload API is not JSON; the API wrapper keeps that media type separate from
        # ordinary requests and makes the publication path hermetic-testable.
        api.upload(url, path.read_bytes())


def publish_release(api: GitHubApi, repository: str, release: dict[str, Any]) -> dict[str, Any]:
    release_id = release.get("id")
    if not release_id:
        raise RuntimeError("GitHub release has no id")
    if release.get("draft"):
        print("==> publishing the verified GitHub release")
        return api.request(
            "PATCH",
            repository_path(repository, f"/releases/{release_id}"),
            {"draft": False},
        )
    print("==> GitHub release is already published")
    return release


def dispatch_pages(api: GitHubApi, repository: str, tag: str) -> None:
    print(f"==> dispatching Pages refresh for {tag}")
    api.request(
        "POST",
        repository_path(repository, "/dispatches"),
        {"event_type": "okf-pages-refresh", "client_payload": {"tag": tag}},
    )


def publish(
    api: GitHubApi,
    repository: str,
    tag: str,
    commit_sha: str,
    assets: list[LocalAsset],
    attempts: int = 40,
    interval: float = 15.0,
) -> None:
    is_release_candidate(tag)
    local, _ = read_local_assets(assets, tag)
    wait_for_mirrored_tag(api, repository, tag, commit_sha, attempts, interval)
    release = release_for_tag(api, repository, tag, commit_sha)
    verify_release_assets(api, release, local)
    upload_missing_assets(api, release, local)
    # Fetch the release again so newly uploaded assets are verified through the same API
    # path as reruns, not merely trusted because the upload returned 201.
    release = api.request("GET", repository_path(repository, f"/releases/tags/{quote(tag, safe='')}"))
    verify_release_assets(api, release, local)
    publish_release(api, repository, release)
    dispatch_pages(api, repository, tag)


def parse_asset(value: str) -> LocalAsset:
    name, separator, path = value.partition("=")
    if not separator or not name or not path:
        raise argparse.ArgumentTypeError("asset must be NAME=PATH")
    return LocalAsset(name, Path(path))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--asset", action="append", type=parse_asset, required=True)
    parser.add_argument("--attempts", type=int, default=40)
    parser.add_argument("--interval", type=float, default=15.0)
    args = parser.parse_args(argv)
    token = __import__("os").environ.get("OKF_GITHUB_TOKEN", "")
    try:
        publish(
            GitHubApi(token),
            args.repository,
            args.tag,
            args.commit,
            args.asset,
            args.attempts,
            args.interval,
        )
    except (ApiError, OSError, RuntimeError, ValueError) as error:
        print(f"github release publication failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
