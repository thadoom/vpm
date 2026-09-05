#!/usr/bin/env python3
"""
Produces the package.json that gets attached to a GitHub Release.

It is the same manifest as the one in the repo, plus the two fields that only
exist once the zip does: the download URL and its SHA256. The listing builder
later reads these straight off each release, so old versions keep working
forever without anyone re-downloading or re-hashing anything.
"""

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--package", required=True, help="Path to the package folder")
    ap.add_argument("--zip", required=True, help="Path to the built zip")
    ap.add_argument("--repo", required=True, help="owner/name")
    ap.add_argument("--tag", required=True)
    ap.add_argument("--pages-url", required=True, help="https://owner.github.io/repo")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    zip_path = Path(args.zip)
    manifest = json.loads((Path(args.package) / "package.json").read_text(encoding="utf-8"))

    # The single most common broken listing: package.json not at the zip root.
    with zipfile.ZipFile(zip_path) as z:
        if "package.json" not in z.namelist():
            print("!! package.json is not at the root of the zip — VCC will install a broken package",
                  file=sys.stderr)
            return 1

    pages = args.pages_url.rstrip("/")
    manifest["url"] = f"https://github.com/{args.repo}/releases/download/{args.tag}/{zip_path.name}"
    manifest["zipSHA256"] = sha256_of(zip_path)
    manifest["changelogUrl"] = f"https://github.com/{args.repo}/blob/main/Packages/{manifest['name']}/CHANGELOG.md"
    manifest["documentationUrl"] = pages
    manifest.setdefault("repo", f"https://github.com/{args.repo}")

    Path(args.out).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"{manifest['name']} {manifest['version']}")
    print(f"  url    {manifest['url']}")
    print(f"  sha256 {manifest['zipSHA256']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
