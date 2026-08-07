# Task 10 — Production TLS authentication smoke test

## Red

Before implementation, `bash scripts/test-production-security-smoke.sh` exited with status 1 at the missing executable check for `scripts/run-production-security-smoke.sh`. This was the expected failure because the smoke script and dedicated CI job did not exist.

## Implementation

- Added `scripts/run-production-security-smoke.sh`.
  - Starts an ephemeral PostgreSQL container from the verified existing digest.
  - Applies EF migrations.
  - Uses the existing Development-only seed path only to create an ephemeral smoke account, then starts a fresh `ASPNETCORE_ENVIRONMENT=Production` API process.
  - Generates a temporary local CA, server key, certificate, and SANs for `localhost`/`127.0.0.1`.
  - Runs the verified Task 9 `nginxinc/nginx-unprivileged:1.30-alpine` digest as the TLS edge.
  - Uses host networking with a dedicated loopback source address for the edge, explicitly clears inherited loopback trust slots, and makes the edge overwrite client-supplied `X-Forwarded-For` with its peer address. Direct smoke requests use a separate loopback source and remain outside the trust model.
  - Exposes a temporary API-only TLS listener for a direct, outside-the-trust-model X-Forwarded-For rate-partition check; the edge remains the path used for the application HTTPS smoke.
  - Checks HTTP-to-HTTPS redirect, CSRF/auth cookie attributes, `/api/auth/me`, the eleventh same-IP login limit despite rotating both `X-Login-Username` and forged `X-Forwarded-For`, and direct forged forwarding-header rejection outside the trusted proxy network.
  - Cleans up the API process, Nginx container, PostgreSQL container, and temporary TLS material.
- Added a separate `production-security-smoke` CI job. The existing Development `e2e-real` job remains unchanged and is still responsible for browser functional coverage.
- Runs the focused smoke contract test in CI before the runtime smoke.

## Green evidence

Passed:

```text
bash -n scripts/run-production-security-smoke.sh
bash scripts/test-production-security-smoke.sh
PYTHONDONTWRITEBYTECODE=1 python3 ... yaml.safe_load(.github/workflows/ci.yml)
node scripts/verify-ci-gates.mjs
bash scripts/run-production-security-smoke.sh
```

The final Docker smoke passed end to end: PostgreSQL migrations, the Production API, temporary CA/TLS, unprivileged Nginx edge, redirect, secure cookie attributes, session restore, both rate-limit checks, and forged forwarding-header rejection. An earlier local attempt exposed the installed .NET SDK/project-reference resolver limitation (`VSHelpDesk.WebAPI.deps.json` was absent); available Release artifacts were then produced by the local build tooling. The repository ledger records the same limitation for the affected .NET suites. Temporary Docker resources were confirmed cleaned up after each attempt.

## Review follow-up

The first review identified three gaps: the forwarding test did not include `X-Forwarded-For`, the focused contract test was not invoked by CI, and this report was missing. A scoped re-review additionally required proving that a forged client address cannot alter the limiter identity. These were addressed before completion: the edge now discards client-supplied `X-Forwarded-For`, the rotated-forgery rate sequence would fail if that address reached the limiter, the direct API check sends both forged forwarding headers and requires an HTTPS redirect, CI runs the focused contract check, and this report records the evidence.
