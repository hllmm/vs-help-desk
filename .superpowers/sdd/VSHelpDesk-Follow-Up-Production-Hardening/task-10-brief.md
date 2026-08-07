# Task 10 — Production TLS authentication smoke test

## Scope

Keep the existing Development real-E2E job unchanged for functional coverage. Add a separate, repeatable smoke path that runs the API in Production behind a pinned unprivileged Nginx TLS edge and exercises the security boundary over HTTPS.

## Required checks

- HTTP requests redirect to HTTPS.
- The pre-login CSRF cookie is `Secure; SameSite=Lax`.
- A successful login sets a secure, HTTP-only, `SameSite=Lax` authentication cookie.
- `/api/auth/me` restores the authenticated session from the cookie.
- The eleventh same-client-IP login request returns 429.
- Changing `X-Login-Username` does not create a new limiter partition.
- A forged forwarding header sent directly to the API does not bypass HTTPS handling or alter the trusted client identity.

## Operational constraints

- The smoke path starts PostgreSQL and applies migrations before starting the Production API.
- TLS material is generated in a temporary directory and never committed.
- The edge image is the verified Task 9 digest for `nginxinc/nginx-unprivileged:1.30-alpine`.
- The existing Development real-E2E job remains functional and separate.
- Test credentials and addresses are ephemeral CI values, not production secrets.

## Required deliverables

- `scripts/run-production-security-smoke.sh`
- focused script/policy tests that fail before the implementation exists
- a separate CI job invoking the smoke script
- a task report with red/green evidence and review findings
