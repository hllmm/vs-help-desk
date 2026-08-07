# Task 9 — Docker base-image pinning and live-upgrade policy report

## Status

Task 9 only. Every repository Dockerfile `FROM` instruction is pinned to a
lowercase 64-hex manifest-list/OCI-index digest. The frontend runtime no
longer performs a live Alpine package upgrade; its existing unprivileged
Nginx setup is preserved.

No application dependencies, runtime API/web image references, or Task 10
changes were made.

## Authoritative registry verification

The following tag-to-digest values were verified against authoritative
registry manifest responses before they were placed in Dockerfiles:

| Image tag | Manifest endpoint | Response media type | `Docker-Content-Digest` |
| --- | --- | --- | --- |
| `mcr.microsoft.com/dotnet/sdk:10.0` | `https://mcr.microsoft.com/v2/dotnet/sdk/manifests/10.0` | `application/vnd.docker.distribution.manifest.list.v2+json` | `sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0` |
| `mcr.microsoft.com/dotnet/aspnet:10.0` | `https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/10.0` | `application/vnd.docker.distribution.manifest.list.v2+json` | `sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b` |
| `node:22-alpine` | `https://registry-1.docker.io/v2/library/node/manifests/22-alpine` | `application/vnd.oci.image.index.v1+json` | `sha256:c610fcdfb1d5b4740dd70c284ed3cb16bb857e0f7166196e36a5501df7a3aa32` |
| `nginxinc/nginx-unprivileged:1.30-alpine` | `https://registry-1.docker.io/v2/nginxinc/nginx-unprivileged/manifests/1.30-alpine` | `application/vnd.oci.image.index.v1+json` | `sha256:44e36330f74d4f3a1d4e222acca9e23b401fb87811a7597024502bb759c4dd49` |

The Docker Hub Nginx value was independently verified from the OCI index and
supplied for this implementation after the earlier registry request timed
out. No digest was inferred from a platform-specific child manifest.

## Policy and CI changes

- Added `scripts/check-dockerfile-pins.sh`, which discovers repository
  `Dockerfile`/`Dockerfile.*` files in deterministic sorted order, requires
  every `FROM` to use `@sha256:` followed by 64 lowercase hex characters, and
  rejects live `apk`, `apt`, `dnf`, `yum`, `zypper`, and `pacman` upgrade forms.
- Added checker fixtures for missing pins, uppercase digests, `apk upgrade`,
  `apt-get upgrade`, `apt upgrade`, `dnf` update/upgrade, `yum` update/upgrade,
  `zypper` update/upgrade, and `pacman -Syu`, plus a safe pinned fixture.
- Added `scripts/test-dockerfile-pins.sh` and a dedicated CI policy job.
- Extended `scripts/verify-ci-gates.mjs` to require both Dockerfile policy
  commands in CI.

## TDD / policy-first evidence

The fixture harness was added before the checker. Its first run was RED
because the checker did not yet exist. After the checker was added, the
unchanged repository produced the expected RED:

```text
$ bash scripts/check-dockerfile-pins.sh
FAIL: Dockerfile:2: FROM must use a lowercase 64-hex @sha256 digest
FAIL: Dockerfile:19: FROM must use a lowercase 64-hex @sha256 digest
FAIL: frontend/Dockerfile:2: FROM must use a lowercase 64-hex @sha256 digest
FAIL: frontend/Dockerfile:13: live package upgrade is forbidden; ...
Dockerfile pin check: FAIL (4 violation(s))
```

The final fixture suite also caught and drove fixes for uppercase digest
acceptance and the case-normalized `pacman -Syu` pattern.

## Verification

```text
$ bash scripts/test-dockerfile-pins.sh
Dockerfile checker fixtures: PASS

$ bash scripts/check-dockerfile-pins.sh
Dockerfile pin check: PASS (2 Dockerfile(s))

$ node scripts/verify-ci-gates.mjs
All CI gates verified.

$ PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
Ran 18 tests ... OK

$ bash scripts/check-networkpolicy-policy.sh
10 tests, 10 passed, 0 warnings, 0 failures, 0 exceptions
NetworkPolicy policy fixtures: PASS

$ bash -n scripts/check-dockerfile-pins.sh scripts/test-dockerfile-pins.sh
exit: 0

$ git diff --check
exit: 0
```

Environment: Linux worktree on branch `production-hardening`; Python YAML,
`kubectl`, and the repository Conftest binary were available. Docker image
builds were not required for this static policy task and were not run.
