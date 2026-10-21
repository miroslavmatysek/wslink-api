---
name: release
description: Release a new version of WsLink.Api - bump the version and compose image tag, commit, tag, build and push the linux/amd64 Docker image to the private registry and create the GitHub release. Use when the user asks to release, publish or ship a version (e.g. "vydej verzi 0.2.0").
---

# Release WsLink.Api

Ask for the version if the user did not give one (`X.Y.Z`, semver). Run the steps in order and stop
when a step fails. Each step that commits, pushes, tags or publishes must have been requested by the user
(a request to "release" covers all of them); report what was done after each push.

## 1. Preconditions

```sh
git switch main && git pull --ff-only
git status --short          # must be empty
dotnet build && dotnet test # must pass
git tag -l "vX.Y.Z"         # must not exist
```

## 2. Version commit

In one commit, so the tag contains a `compose.yaml` that points to its own image:

- `src/WsLink.Api/WsLink.Api.csproj`: set `Version`, `AssemblyVersion` and `FileVersion` to `X.Y.Z`
  (no other project holds the version),
- `compose.yaml`: set both image tags (`image:` and the commented `#image:`) to `vX.Y.Z`.

Build to check, then commit `set version X.Y.Z` and push to `main`.

## 3. Tag

Annotated tag on the version commit, then push it:

```sh
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
```

Never move or recreate a tag that is already pushed.

## 4. Docker image

The server is `linux/amd64`; a build without `--platform` on Apple Silicon produces arm64. Always build
with an explicit platform and push directly; never `docker compose push`:

```sh
git describe --tags --exact-match   # must print vX.Y.Z, so the image matches the tag
docker buildx build --platform linux/amd64 -t 192.168.88.28:5000/wslink-api:vX.Y.Z --push .
```

Verify:

```sh
curl -s http://192.168.88.28:5000/v2/wslink-api/tags/list
curl -s -H "Accept: application/vnd.oci.image.index.v1+json" \
  http://192.168.88.28:5000/v2/wslink-api/manifests/vX.Y.Z   # platform must be linux/amd64
docker rm -f wslink-smoke 2>/dev/null                       # leftover from a failed run
docker run -d --name wslink-smoke --platform linux/amd64 -p 18080:8080 192.168.88.28:5000/wslink-api:vX.Y.Z
curl -s -o /dev/null -w "%{http_code}" http://localhost:18080/health   # 200, retry while starting
docker logs wslink-smoke | grep Version                                 # shows X.Y.Z
docker rm -f wslink-smoke
```

If port `18080` is taken, use another free port.

## 5. GitHub release

Write release notes from `git log --oneline <previous tag>..vX.Y.Z` (read the commits, not only subjects):

- `## Highlights` – the main user-visible changes,
- `## Changes` – grouped by `### Home Assistant`, `### Windy`, `### openHAB`, `### General`,
- `## Upgrade notes` – behavior changes that can break existing setups (payload changes, new required config),
- `## Docker` – image tag and platform; do not include the registry address (the release is public),
- `**Full changelog**: https://github.com/miroslavmatysek/wslink-api/compare/<previous tag>...vX.Y.Z`.

Save the notes to the scratchpad, then:

```sh
gh release create vX.Y.Z --verify-tag --title "vX.Y.Z" --notes-file <notes file>
gh release view vX.Y.Z --json tagName,isDraft,isPrerelease,url
```
