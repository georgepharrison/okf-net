#!/usr/bin/env python3
"""Hermetic tests for the GitHub release and Pages helpers."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import prepare_github_pages
import publish_github_release


REPOSITORY = "georgepharrison/okf-net"
TAG = "v2.3.0-rc.10"
COMMIT = "a" * 40


class FakeApi:
    def __init__(self, release: dict, releases: list[dict] | None = None, tag_sha: str = COMMIT):
        self.release = release
        self.releases = releases or [release]
        self.tag_sha = tag_sha
        self.calls: list[tuple[str, str]] = []
        self.downloads: dict[str, bytes] = {}

    def request(self, method: str, path: str, body=None, accept: str = ""):
        self.calls.append((method, path))
        if path.endswith(f"/git/ref/tags/{TAG}"):
            return {"object": {"sha": self.tag_sha, "type": "commit"}}
        if path.endswith(f"/releases/tags/{TAG}"):
            return self.release
        if path.endswith("/releases?per_page=100&page=1"):
            return self.releases
        if path.endswith("/releases?per_page=100&page=2"):
            return []
        if method == "POST" and path.endswith("/releases"):
            return self.release
        if method == "PATCH" and "/releases/" in path:
            self.release["draft"] = body["draft"]
            return self.release
        if method == "POST" and path.endswith("/dispatches"):
            return None
        raise AssertionError(f"unexpected API call {method} {path}")

    def upload(self, url: str, data: bytes) -> None:
        name = parse_qs(urlparse(url).query)["name"][0]
        asset_url = f"https://api.github.com/assets/{TAG}/{name}"
        self.release.setdefault("assets", []).append({"name": name, "url": asset_url})
        self.downloads[asset_url] = data
        self.calls.append(("UPLOAD", name))

    def download(self, url: str) -> bytes:
        self.calls.append(("DOWNLOAD", url))
        return self.downloads[url]


def make_release_tree(root: Path, tag: str = TAG) -> tuple[list[publish_github_release.LocalAsset], dict[str, bytes]]:
    root.mkdir(parents=True, exist_ok=True)
    assets: dict[str, bytes] = {
        name: f"bytes for {name}".encode("utf-8") for name in publish_github_release.EXPECTED_ASSETS
    }
    manifest_assets = {}
    for name in publish_github_release.EXPECTED_ASSETS:
        if name == "latest.json":
            continue
        manifest_assets[name] = {
            "path": f"v{tag.removeprefix('v')}/{name}",
            "size": len(assets[name]),
            "sha256": hashlib.sha256(assets[name]).hexdigest(),
            "url": f"https://gitlab.example/packages/{name}",
            "downloadUrl": prepare_github_pages.public_asset_url(REPOSITORY, tag, name),
        }
    assets["latest.json"] = json.dumps(
        {"version": tag.removeprefix("v"), "tag": tag, "generatedAt": "2026-01-01T00:00:00Z", "assets": manifest_assets},
        indent=2,
    ).encode("utf-8")
    paths = []
    for name, data in assets.items():
        path = root / name
        path.write_bytes(data)
        paths.append(publish_github_release.LocalAsset(name, path))
    return paths, assets


def release_for(tag: str, assets: dict[str, bytes], draft: bool = True) -> dict:
    metadata = []
    for name in assets:
        url = f"https://api.github.com/assets/{tag}/{name}"
        metadata.append({"name": name, "url": url})
    return {
        "id": 7,
        "tag_name": tag,
        "draft": draft,
        "prerelease": "-rc." in tag,
        "upload_url": "https://uploads.github.com/repos/example/releases/7/assets{?name,label}",
        "assets": metadata,
    }


class GitHubApiTests(unittest.TestCase):
    def test_asset_redirects_strip_authorization_and_refuse_untrusted_destinations(self):
        handler = publish_github_release.SafeDownloadRedirectHandler()
        request = publish_github_release.Request(
            "https://api.github.com/repos/example/releases/assets/7",
            headers={"Authorization": "Bearer secret"},
        )

        redirected = handler.redirect_request(
            request,
            None,
            302,
            "Found",
            {},
            "https://release-assets.githubusercontent.com/signed-asset",
        )

        self.assertIsNotNone(redirected)
        self.assertIsNone(redirected.get_header("Authorization"))
        with self.assertRaisesRegex(RuntimeError, "untrusted URL"):
            handler.redirect_request(
                request,
                None,
                302,
                "Found",
                {},
                "https://downloads.example.com/capture-token",
            )
        with self.assertRaisesRegex(RuntimeError, "untrusted URL"):
            handler.redirect_request(
                request,
                None,
                302,
                "Found",
                {},
                "http://release-assets.githubusercontent.com/downgrade",
            )


class GitHubReleaseTests(unittest.TestCase):
    def test_publish_waits_for_matching_tag_verifies_all_bytes_and_dispatches_pages(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, data = make_release_tree(Path(directory))
            release = release_for(TAG, data)
            api = FakeApi(release)
            api.downloads = {
                f"https://api.github.com/assets/{TAG}/{name}": content for name, content in data.items()
            }

            publish_github_release.publish(api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0)

            self.assertFalse(release["draft"])
            self.assertIn(("POST", f"/repos/{REPOSITORY}/dispatches"), api.calls)
            self.assertEqual(
                [name for name in publish_github_release.EXPECTED_ASSETS
                 if ("DOWNLOAD", f"https://api.github.com/assets/{TAG}/{name}") in api.calls],
                list(publish_github_release.EXPECTED_ASSETS),
            )

    def test_uploads_missing_assets_then_verifies_the_uploaded_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, data = make_release_tree(Path(directory))
            release = release_for(TAG, data)
            release["assets"] = []
            api = FakeApi(release)

            publish_github_release.publish(api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0)

            self.assertEqual(
                list(publish_github_release.EXPECTED_ASSETS),
                [name for action, name in api.calls if action == "UPLOAD"],
            )
            self.assertFalse(release["draft"])

    def test_refuses_to_fill_an_incomplete_release_that_is_already_public(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, data = make_release_tree(Path(directory))
            release = release_for(TAG, data, draft=False)
            release["assets"] = []
            api = FakeApi(release)

            with self.assertRaisesRegex(RuntimeError, "refusing to mutate it outside a draft"):
                publish_github_release.publish(
                    api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0
                )

            self.assertFalse(any(action == "UPLOAD" for action, _ in api.calls))

    def test_refuses_an_existing_release_with_the_wrong_prerelease_state(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, data = make_release_tree(Path(directory))
            release = release_for(TAG, data)
            release["prerelease"] = False
            api = FakeApi(release)

            with self.assertRaisesRegex(RuntimeError, "expected True"):
                publish_github_release.publish(
                    api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0
                )

    def test_refuses_a_tag_that_is_not_stable_or_strict_rc(self):
        for invalid_tag in (
            "v2.3.0-rc.1-extra",
            "v02.3.0",
            "v2.03.0",
            "v2.3.00",
            "v2.3.0-rc.01",
        ):
            with self.subTest(tag=invalid_tag), tempfile.TemporaryDirectory() as directory:
                paths, _ = make_release_tree(Path(directory), invalid_tag)
                api = FakeApi({}, tag_sha=COMMIT)

                with self.assertRaisesRegex(RuntimeError, "neither a stable semver tag"):
                    publish_github_release.publish(
                        api,
                        REPOSITORY,
                        invalid_tag,
                        COMMIT,
                        paths,
                        attempts=1,
                        interval=0,
                    )
                self.assertIsNone(prepare_github_pages.version_key(invalid_tag))

    def test_refuses_a_download_url_outside_the_matching_github_release(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, _ = make_release_tree(Path(directory))
            manifest_path = next(asset.path for asset in paths if asset.name == "latest.json")
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["assets"]["okf-linux-x64"]["downloadUrl"] = (
                "https://downloads.example.com/okf-linux-x64"
            )
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            api = FakeApi({}, tag_sha=COMMIT)

            with self.assertRaisesRegex(RuntimeError, "downloadUrl for okf-linux-x64"):
                publish_github_release.publish(
                    api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0
                )

            self.assertNotIn(("GET", f"/repos/{REPOSITORY}/git/ref/tags/{TAG}"), api.calls)

    def test_refuses_a_tag_that_points_at_a_different_commit_before_release_creation(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, _ = make_release_tree(Path(directory))
            api = FakeApi({}, tag_sha="b" * 40)

            with self.assertRaisesRegex(RuntimeError, "not CI_COMMIT_SHA"):
                publish_github_release.publish(api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0)

            self.assertNotIn(("POST", f"/repos/{REPOSITORY}/releases"), api.calls)

    def test_refuses_differing_existing_asset_instead_of_replacing_it(self):
        with tempfile.TemporaryDirectory() as directory:
            paths, data = make_release_tree(Path(directory))
            release = release_for(TAG, data)
            api = FakeApi(release)
            api.downloads = {
                f"https://api.github.com/assets/{TAG}/{name}": (b"tampered" if name == "install.sh" else content)
                for name, content in data.items()
            }

            with self.assertRaisesRegex(RuntimeError, "differs"):
                publish_github_release.publish(api, REPOSITORY, TAG, COMMIT, paths, attempts=1, interval=0)

            self.assertNotIn(("POST", f"/repos/{REPOSITORY}/dispatches"), api.calls)


class PagesPreparationTests(unittest.TestCase):
    def test_stages_a_release_candidate_without_faking_a_missing_stable_alias(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _, rc_data = make_release_tree(root / "rc-source", TAG)
            release = release_for(TAG, rc_data, draft=False)
            api = FakeApi(release, releases=[release])
            api.downloads = {
                f"https://api.github.com/assets/{TAG}/{name}": content
                for name, content in rc_data.items()
            }

            out = root / "public"
            prepare_github_pages.prepare(api, REPOSITORY, out)

            self.assertTrue((out / "dev" / "latest.json").exists())
            self.assertFalse((out / "stable").exists())
            self.assertFalse((out / "latest.json").exists())

    def test_selects_numeric_versions_and_stages_only_small_public_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stable_paths, stable_data = make_release_tree(root / "stable-source", "v2.2.0")
            rc9_paths, rc9_data = make_release_tree(root / "rc9-source", "v2.3.0-rc.9")
            rc10_paths, rc10_data = make_release_tree(root / "rc10-source", TAG)
            releases = [
                release_for("v2.3.0-rc.9", rc9_data, draft=False),
                release_for(TAG, rc10_data, draft=False),
                release_for("v2.2.0", stable_data, draft=False),
            ]
            api = FakeApi(releases[0], releases=releases)
            for tag, data in (
                ("v2.2.0", stable_data),
                ("v2.3.0-rc.9", rc9_data),
                (TAG, rc10_data),
            ):
                for name, content in data.items():
                    api.downloads[f"https://api.github.com/assets/{tag}/{name}"] = content

            out = root / "public"
            prepare_github_pages.prepare(api, REPOSITORY, out)

            self.assertEqual((out / "stable" / "latest.json").read_bytes(), stable_data["latest.json"])
            self.assertEqual((out / "dev" / "latest.json").read_bytes(), rc10_data["latest.json"])
            for tag in ("v2.2.0", "v2.3.0-rc.9", TAG, "stable", "dev"):
                self.assertEqual(
                    sorted(path.name for path in (out / tag).iterdir()),
                    sorted(prepare_github_pages.SMALL_ASSETS),
                )
            self.assertEqual((out / "latest.json").read_bytes(), stable_data["latest.json"])
            self.assertFalse((out / TAG / "okf-linux-x64").exists())
            self.assertFalse((out / TAG / "okf-net-knowledge.tar.gz").exists())


if __name__ == "__main__":
    unittest.main()
