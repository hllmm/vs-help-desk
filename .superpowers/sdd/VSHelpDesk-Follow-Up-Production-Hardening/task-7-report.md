# Task 7 — Explicit production mail egress rendering report

## Status

Implemented Task 7 only. Production rendering now requires an explicit
`MAIL_EGRESS_MODE` and generates `api-mail-egress` only when enabled with
operator-supplied SMTP and IMAP relay CIDRs. The base `api-allow` policy and
all base NetworkPolicy files remain unchanged.

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
- Disabled rendering emits no mail-egress policy and does not require relay
  variables.
- Updated CI render invocations to pass `MAIL_EGRESS_MODE=disabled`.
- Documented the exact renderer variable contract in
  `docs/deploy-kubernetes.md`.
- Added direct generator and renderer integration tests.

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

## Final verification

All required focused checks passed:

```text
Enabled/disabled YAML parsing and exact api-allow preservation: PASS
  19 enabled documents; 18 disabled documents.

kubectl kustomize deploy/k8s/base: PASS
kubectl kustomize deploy/k8s/overlays/prod: PASS
bash scripts/check-networkpolicy-policy.sh: PASS
  10 Conftest tests passed in both single and combined base checks.
bash scripts/check-prod-manifest.sh on disabled render: PASS
node scripts/verify-ci-gates.mjs: PASS
node scripts/check-markdown-links.mjs: PASS
bash -n scripts/render-prod-manifest.sh: PASS
Python compilation: PASS
git diff --check: PASS
```

The enabled render structural check also verified the API selector, Egress
policy type, separate SMTP/IMAP rules, all required ports, canonical CIDRs,
and absence of `api-mail-egress` in disabled mode.

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
- A direct single-document Conftest probe of an enabled rendered manifest is
  not part of the existing CI harness: the pre-existing Task 5 generic rule
  intentionally rejects SMTP/IMAP `ipBlock` egress. The required base-policy
  single and combined harnesses pass, and CI renders Task 7 in disabled mode
  because no production relay CIDRs may be invented in CI.

## Changed files

- `.github/workflows/ci.yml`
- `docs/deploy-kubernetes.md`
- `scripts/generate_mail_egress_policy.py`
- `scripts/render-prod-manifest.sh`
- `scripts/test_generate_mail_egress_policy.py`
- `scripts/test_render_prod_manifest.py`
- `.superpowers/sdd/VSHelpDesk-Follow-Up-Production-Hardening/task-7-report.md`
