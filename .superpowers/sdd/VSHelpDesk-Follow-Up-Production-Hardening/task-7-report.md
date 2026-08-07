# Task 7 — Explicit production mail egress rendering report

## Status

Implemented Task 7 and its scoped review fix round. Production rendering now
requires an explicit `MAIL_EGRESS_MODE` and generates `api-mail-egress` only
when enabled with operator-supplied SMTP and IMAP relay CIDRs. The base
`api-allow` policy and all base NetworkPolicy files remain unchanged.

## Changes

- Added `scripts/generate_mail_egress_policy.py` using Python's standard
  library `ipaddress.ip_network(..., strict=False)` for every comma-separated
  relay entry.
- Rejects missing relay lists, empty entries, malformed CIDRs, and semantic
  world CIDRs, including equivalent non-canonical `/0` spellings. Valid
  entries are rendered in canonical form.
- Enabled rendering appends a separate namespaced `api-mail-egress` policy
  selecting API pods, with SMTP TCP ports `25`, `465`, `587` and IMAP TCP ports
  `143`, `993`.
- The generated policy carries the exact provenance annotation
  `vshelpdesk.io/policy-provenance: task-7-mail-egress-generator`. Task 5
  policy-as-code uses the marker to distinguish renderer-owned mail egress
  from ordinary policies; world CIDR checks remain unconditional.
- Disabled rendering emits no mail-egress policy and does not require relay
  variables.
- Updated CI render invocations to pass `MAIL_EGRESS_MODE=disabled`.
- Documented the exact renderer variable contract in
  `docs/deploy-kubernetes.md`.
- Added direct generator, renderer, Conftest integration, and fail-closed
  policy fixture tests.

No production image values, fake digests, base policies, dependencies, or
secrets were changed.

## TDD evidence

The new tests were written before the generator and renderer changes.

### RED

```text
$ python3 scripts/test_generate_mail_egress_policy.py
Ran 5 tests in 0.094s
FAILED (failures=6)
Cause: generate_mail_egress_policy.py did not exist.

$ python3 scripts/test_render_prod_manifest.py
Ran 6 tests in 1.149s
FAILED (failures=8)
Cause: the existing renderer ignored MAIL_EGRESS_MODE, relay inputs, and
enabled policy generation. The disabled no-policy characterization passed.
```

### GREEN

```text
$ PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
Ran 11 tests in 1.020s
OK
```

## Review fix round

### Root cause

The generated policy used explicit relay CIDRs but had no provenance marker.
Task 5 therefore correctly treated it like every other mail-CIDR policy and
reported `SMTP/IMAP egress with an ipBlock is forbidden`. The exception is now
limited to the exact policy name `api-mail-egress` plus the exact annotation
key/value documented above. Base policies, renamed policies, wrong-marker
policies, and marker-bearing world CIDRs remain rejected.

### RED against `5f970ca`

```text
$ git rev-parse --short HEAD
5f970ca

$ python3 scripts/test_render_prod_manifest.py RenderProdManifestTests.test_enabled_render_passes_conftest_but_ordinary_mail_policy_rejects
Ran 1 test in 0.234s
FAILED (failures=1)
Cause: the enabled render was rejected by Conftest with
SMTP/IMAP egress with an ipBlock is forbidden.
```

### GREEN

```text
$ python3 scripts/test_render_prod_manifest.py RenderProdManifestTests.test_enabled_render_passes_conftest_but_ordinary_mail_policy_rejects
Ran 1 test in 0.240s
OK

$ PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
Ran 12 tests in 1.152s
OK
```

## Final verification

All required focused checks passed:

```text
Enabled/disabled YAML parsing and exact api-allow preservation: PASS
  19 enabled documents; 18 disabled documents.

kubectl kustomize deploy/k8s/base: PASS
kubectl kustomize deploy/k8s/overlays/prod: PASS
bash scripts/check-networkpolicy-policy.sh: PASS
  13 expected fixture rejections, plus base single and combined acceptance.
Conftest on enabled rendered manifest: PASS
  Ordinary mail-CIDR fixture still rejects; wrong-marker and
  marker-bearing-world fixtures reject.
bash scripts/check-prod-manifest.sh on disabled render: PASS
node scripts/verify-ci-gates.mjs: PASS
node scripts/check-markdown-links.mjs: PASS
bash -n scripts/render-prod-manifest.sh: PASS
Python compilation: PASS
git diff --check: PASS
```

The enabled render checks also verified the exact provenance annotation, API
selector, Egress policy type, separate SMTP/IMAP rules, all required ports,
canonical CIDRs, and absence of `api-mail-egress` in disabled mode.

## Environment notes and non-green exploratory checks

- Python `3.14.6`.
- kubectl `v1.36.2`, embedded Kustomize `v5.8.1`.
- Conftest `0.69.0` with OPA `1.19.0` was available locally.
- PyYAML was available for rendered multi-document YAML parsing.
- `shellcheck` was unavailable; Bash syntax validation passed with `bash -n`.
- An attempted `kubectl apply/create --dry-run=client` stdin validation tried
  to contact the unavailable local API server at `localhost:8080` and failed
  with connection refused. The renderer output was instead parsed locally by
  PyYAML, which passed all structural assertions.
- The initial enabled-render Conftest failure described in the review-round
  RED evidence is resolved by the exact provenance exception; no production
  relay CIDRs are added to CI.

## Changed files

- `.github/workflows/ci.yml`
- `docs/deploy-kubernetes.md`
- `scripts/generate_mail_egress_policy.py`
- `scripts/render-prod-manifest.sh`
- `scripts/test_generate_mail_egress_policy.py`
- `scripts/test_render_prod_manifest.py`
- `policy/networkpolicy/networkpolicy.rego`
- `policy/networkpolicy/fixtures/unsafe-generated-mail-policy-world.yaml`
- `policy/networkpolicy/fixtures/unsafe-generated-mail-policy-wrong-name.yaml`
- `policy/networkpolicy/fixtures/unsafe-generated-mail-policy-wrong-marker.yaml`
- `scripts/check-networkpolicy-policy.sh`
- `.superpowers/sdd/VSHelpDesk-Follow-Up-Production-Hardening/task-7-report.md`
