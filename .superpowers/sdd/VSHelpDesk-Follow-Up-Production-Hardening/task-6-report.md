# Task 6 report — Correct base Kubernetes NetworkPolicies

Date: 2026-08-07
Branch: production-hardening

## Scope

Task 6 only. The base policies now retain the intended ingress-controller → web,
web → API, jobs → API, API → PostgreSQL, and required DNS flows. Mail egress
generation remains deferred to Task 7.

## Changes

- The policy harness renders deploy/k8s/base with kubectl kustomize and requires
  Conftest acceptance.
- Replaced commonLabels with a selector-safe labels transformer that preserves
  app.kubernetes.io/part-of on resource metadata without changing selectors.
- Added jobs-allow-egress for same-namespace API TCP 8080 and kube-dns UDP/TCP
  53 access.
- The web ingress controller uses one peer containing both the ingress-nginx
  namespace selector and pod selector.
- Removed web ingress from 10.0.0.0/8, world ingress used for probes, example
  SMTP/IMAP relay CIDRs, PostgreSQL egress, and same-namespace DNS fallbacks.
- Removed the stale known-limitations paragraph that documented example mail
  relay CIDRs.

## TDD/policy-first evidence

Before the YAML changes, the updated harness failed against the rendered base
with five unsafe findings: world CIDR ingress, separate selector semantics,
SMTP/IMAP ipBlock egress, and both example relay CIDRs. The run reported
68 tests, 63 passed, 0 warnings, 0 failures, 0 exceptions for the policy
engine and exited nonzero as expected.

After the YAML changes, the same harness passed all unsafe fixture rejection
checks, the rendered-base acceptance check, and both safe fixtures.

## Reviewer-fix round RED/GREEN evidence

The corrected combined rendered-policy check was run against an archived
render of commit 2419514. It exited nonzero with six expected denials:
contaminated ingress-nginx and kube-dns selectors, missing jobs egress to API,
missing jobs DNS UDP/TCP access, and missing jobs-allow-egress policy.

After the fixes, the worktree policy harness passed its fixture checks and both
single-resource and combined rendered-base acceptance checks. Independent
rendered structural assertions passed for exact external selectors, jobs API
egress, and jobs kube-dns UDP/TCP 53 egress.

## Verification

- bash scripts/check-networkpolicy-policy.sh — PASS.
- kubectl kustomize deploy/k8s/base >/dev/null — PASS.
- kubectl kustomize deploy/k8s/overlays/prod >/dev/null — PASS.
- kubectl kustomize deploy/k8s/overlays/prod | bash scripts/check-prod-manifest.sh — PASS.
- node scripts/verify-ci-gates.mjs — PASS; all 21 checks passed.
- git diff --check — PASS.

## Environment

No blocking environment limitation. Local kubectl, Conftest 0.69.0, and OPA
1.19.0 were available. The final base and production renders emitted no
Kustomize deprecation warning.
