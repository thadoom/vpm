# Maintaining this repository

This repo is two things at once: the Unity package, and the VPM listing that serves it.
GitHub Actions does the rest. Nothing is hardcoded to a username or repo name — every URL is
derived from the GitHub context.

```
Packages/com.thadoom.lucent/   the Unity package
Website/index.html             landing page published to Pages
Tools/                         the two build scripts the Actions call
listing.config.json            listing name / id / author
.github/workflows/             release.yml + listing.yml
```

---

## One-time setup

1. Push to `main`.
2. Settings → Actions → General → Workflow permissions → **Read and write**.
   Without this the release workflow cannot create releases.
3. Settings → Pages → Source → **GitHub Actions**.

Step 3 cannot be automated. `GITHUB_TOKEN` is not allowed to create a Pages site: the API
answers 403 "Resource not accessible by integration" regardless of the permissions the workflow
declares, so `actions/configure-pages` is deliberately used without `enablement: true`. Switch
Pages on once by hand and every run after that is automatic.

---

## Publishing a version

```bash
# 1. bump the version in Packages/com.thadoom.lucent/package.json
# 2. write what changed in that package's CHANGELOG.md
git commit -am "1.0.1"
git tag v1.0.1
git push && git push origin v1.0.1
```

Or run the **Release** workflow manually from the Actions tab — it reads the version from
`package.json` and creates the tag itself.

`release.yml`:

- refuses if the tag disagrees with `package.json`'s version,
- refuses if that version is already released (a published version is immutable),
- zips the package **with `package.json` at the zip root** and verifies it,
- computes `zipSHA256`, injects it and the download URL into a `package.json` release asset,
- publishes the GitHub Release using the CHANGELOG as release notes.

`listing.yml` then fires on the published release, reads **every** release in the repo, and rebuilds
`index.json` from their `package.json` assets before deploying Pages.

---

## The releases are the database

`build_listing.py` keeps no state. It asks GitHub for the list of releases and reads the
`package.json` asset attached to each — which already contains that version's `url` and
`zipSHA256`, frozen at publish time.

The good consequence: delete `index.json`, wipe Pages, rebuild on a fresh runner, and every version
ever published comes straight back.

The consequence to respect: **deleting a GitHub Release deletes that version from the listing.**
Anyone whose `Packages/vpm-manifest.json` pins it will find the project no longer resolves. If a
release is broken, publish a fixed version on top of it. Never delete, never re-tag.

---

## The three things that break a listing

1. **`package.json` not at the root of the zip.** If the zip contains
   `com.thadoom.lucent/package.json`, VCC installs a broken package. `release.yml` zips the
   *contents* of the package folder and `make_release_manifest.py` hard-fails if `package.json`
   is not at the root.

2. **A stale `zipSHA256`.** VCC verifies the hash before installing and fails silently if it
   disagrees. The hash is computed from the finished zip in the step that publishes it, so it
   cannot drift.

3. **A cached `index.json`.** Pages serves it with a short cache, so a new version can take a
   minute or two to appear. In VCC: **Settings → Packages → Refresh**.

---

## The listing id

`listing.config.json` carries `com.thadoom.vpm`. Once anyone has added the listing, that id must
never change — VCC keys the repository by it. The display name is free to change.

---

## Local dry run

```bash
cd Packages/com.thadoom.lucent && zip -r /tmp/test.zip . && cd -
python3 Tools/make_release_manifest.py \
  --package Packages/com.thadoom.lucent --zip /tmp/test.zip \
  --repo thadoom/vpm --tag v1.0.0 \
  --pages-url https://thadoom.github.io/vpm --out /tmp/package.json

python3 Tools/build_listing.py \
  --repo thadoom/vpm \
  --pages-url https://thadoom.github.io/vpm --out /tmp/_site
```

The listing builder works against a public repo without a token; set `GITHUB_TOKEN` if you hit the
rate limit.
