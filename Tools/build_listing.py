#!/usr/bin/env python3
"""
Builds index.json — the file VCC actually reads — from this repo's GitHub Releases.

The releases ARE the database. Every release carries a package.json asset that
already has its url and zipSHA256 baked in, so this script only has to collect
them. Two consequences worth knowing:

  * Old versions can never be lost. Rebuild the listing from scratch on a fresh
    runner and every version ever published comes back, because they live in the
    releases, not in a file someone might overwrite.
  * Deleting a GitHub Release DOES delete that version from the listing, and that
    breaks any project whose vpm-manifest.json pins it. Don't delete releases.
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path


def gh_get(url: str, token: str | None, as_json: bool = True):
    req = urllib.request.Request(url)
    req.add_header("Accept", "application/vnd.github+json" if as_json else "application/octet-stream")
    req.add_header("X-GitHub-Api-Version", "2022-11-28")
    req.add_header("User-Agent", "vpm-listing-builder")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
    return json.loads(data) if as_json else data


def fetch_releases(repo: str, token: str | None) -> list:
    releases, page = [], 1
    while True:
        url = f"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}"
        batch = gh_get(url, token)
        if not batch:
            break
        releases.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    return releases


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", required=True, help="owner/name")
    ap.add_argument("--pages-url", required=True, help="https://owner.github.io/repo")
    ap.add_argument("--config", default="listing.config.json")
    ap.add_argument("--out", default="_site")
    args = ap.parse_args()

    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    cfg = json.loads(Path(args.config).read_text(encoding="utf-8"))
    pages = args.pages_url.rstrip("/")

    index = {
        "name": cfg["name"],
        "id": cfg["id"],
        "url": f"{pages}/index.json",
        "author": cfg["author"],
        "packages": {},
    }

    releases = fetch_releases(args.repo, token)
    print(f"{len(releases)} releases in {args.repo}")

    kept = 0
    for rel in releases:
        if rel.get("draft"):
            continue
        asset = next((a for a in rel.get("assets", []) if a["name"] == "package.json"), None)
        if asset is None:
            print(f"  skip {rel.get('tag_name')}: no package.json asset")
            continue
        try:
            manifest = json.loads(gh_get(asset["browser_download_url"], token, as_json=False))
        except (urllib.error.URLError, json.JSONDecodeError) as e:
            print(f"  skip {rel.get('tag_name')}: {e}", file=sys.stderr)
            continue

        missing = [f for f in ("name", "displayName", "version", "url", "author") if f not in manifest]
        if missing:
            print(f"  skip {rel.get('tag_name')}: manifest missing {missing}", file=sys.stderr)
            continue
        if "zipSHA256" not in manifest:
            print(f"  warn {rel.get('tag_name')}: no zipSHA256, VCC cannot verify the download", file=sys.stderr)

        pkg_id, version = manifest["name"], manifest["version"]
        index["packages"].setdefault(pkg_id, {}).setdefault("versions", {})
        index["packages"][pkg_id]["versions"][version] = manifest
        kept += 1
        print(f"  + {pkg_id} {version}")

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "index.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")

    print(f"\n{kept} versions across {len(index['packages'])} packages")
    print(f"Listing URL: {pages}/index.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
