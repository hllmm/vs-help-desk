# Task 8 — Structured production image validation report

## Status

Implemented Task 8 only. Production image validation now evaluates the parsed
Deployment structure through Conftest/OPA. The checked-in production overlay
contains unresolved local image references and is intentionally rejected;
`render-prod-manifest.sh` accepts only operator-supplied, allow-listed
`@sha256:` references and validates the rendered artifact before writing it.

## Policy coverage

`policy/production/production.rego` requires exactly one API Deployment and
one web Deployment, verifies their workload names and single-container image
structure, and accepts only these existing repository paths:

- `ghcr.io/vs-help-desk/api`
- `ghcr.io/vs-help-desk/web`

Images must use lowercase 64-hex `@sha256:` digests. Mutable `latest`, `local`,
and `stable` tags, all-`a`/all-`b` digests, missing/duplicate deployments,
wrong kinds, missing images, and disallowed repositories are rejected. Each
target Deployment must have an array-valued `spec.template.spec.containers`
with exactly one regular container. Optional `initContainers` must also be an
array; every init-container entry must be an object with an image that passes
the same workload-specific repository, digest, mutable-tag, and placeholder
checks.

## TDD / policy-first evidence

The structured fixtures and tests were added before the validator integration.
The current regex checker could not distinguish duplicate or wrong workload
structures and accepted the overlay's regex-valid fake SHA tags; the new
policy test suite then failed until the Conftest policy, overlay, and renderer
changes were implemented.

## Verification

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_prod_manifest_policy.py
Ran 3 tests ... OK

$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_render_prod_manifest.py
Ran 10 tests ... OK

$ kubectl kustomize deploy/k8s/overlays/prod | bash scripts/check-prod-manifest.sh
exit: 1 (expected; unrendered local images are not deployable)

$ bash scripts/check-networkpolicy-policy.sh
NetworkPolicy policy fixtures: PASS

$ git diff --check
exit: 0
```

Positive structural tests use non-deployable test-only digest strings solely
to exercise parsing and allow-list logic; no such values are present in the
production overlay, CI configuration, or deployment artifact. Real verified
image references remain operator inputs to the renderer.

## Reviewer fix round — initContainers and malformed container lists

### Root cause

The first policy version only counted and indexed the regular container list
and never inspected `initContainers`. A map-valued `containers` field with one
key could therefore satisfy `count(containers) == 1`, and a mutable or
disallowed init image was invisible to the image checks.

### TDD evidence

Four fixtures and the policy test were added before the policy fix:

- `unsafe-api-init-latest.yaml`
- `unsafe-web-init-disallowed-repository.yaml`
- `unsafe-api-containers-not-array.yaml`
- `unsafe-api-init-containers-not-array.yaml`

RED against the committed validator:

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_prod_manifest_policy.py
Ran 3 tests ... FAILED (failures=4)
Cause: all four new fixtures incorrectly returned exit 0 with
"check-prod-manifest: PASS (structured production image policy)".
```

GREEN after the Rego fix:

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_prod_manifest_policy.py
Ran 3 tests ... OK

Direct policy diagnostics:
  init latest: initContainer image rejected and :latest rejected
  init disallowed repository: initContainer image rejected
  malformed containers: spec.template.spec.containers must be an array
  malformed initContainers: spec.template.spec.initContainers must be an array
```

The fix does not alter renderer behavior, production image values, or Task 7
mail policy generation.

### Follow-up hardening: regular container entry type

The review identified one remaining malformed-list case: an array containing a
scalar entry. The fixture `unsafe-api-container-entry-not-object.yaml` was
added before the policy update.

RED against the then-current policy:

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_prod_manifest_policy.py
Ran 3 tests ... FAILED (failures=1)
Cause: the scalar-entry fixture returned exit 0 with
"check-prod-manifest: PASS (structured production image policy)".
```

GREEN after adding the regular-container entry object/image deny:

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_prod_manifest_policy.py
Ran 3 tests ... OK

$ bash scripts/check-prod-manifest.sh policy/production/fixtures/unsafe-api-container-entry-not-object.yaml
FAIL: api Deployment containers entries must be objects with an image
12 tests, 11 passed, 0 warnings, 1 failure, 0 exceptions
```

Fix-round regression verification:

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_render_prod_manifest.py
Ran 10 tests ... OK
$ PYTHONDONTWRITEBYTECODE=1 python3 scripts/test_generate_mail_egress_policy.py
Ran 5 tests ... OK
$ PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
Ran 18 tests ... OK
$ kubectl kustomize deploy/k8s/base >/dev/null
PASS
$ kubectl kustomize deploy/k8s/overlays/prod >/dev/null
PASS
```

## Changed files

- `policy/production/production.rego`
- `policy/production/fixtures/*.yaml`
- `scripts/check-prod-manifest.sh`
- `scripts/test_prod_manifest_policy.py`
- `scripts/render-prod-manifest.sh`
- `scripts/test_render_prod_manifest.py`
- `deploy/k8s/overlays/prod/kustomization.yaml`
- `.github/workflows/ci.yml`
- `docs/deploy-kubernetes.md`
- `.superpowers/sdd/VSHelpDesk-Follow-Up-Production-Hardening/task-8-report.md`

The Task 7 mail-egress mode and generated-policy provenance behavior remain
unchanged.
