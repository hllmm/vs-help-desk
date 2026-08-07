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
wrong kinds, missing images, and disallowed repositories are rejected.

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

The Task 7 mail-egress mode and generated-policy provenance behavior remain
unchanged.
