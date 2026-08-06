# Security Verification Checklist (Manual Runbook)

Hardening closure for `docs/security-verification.md` (SEC-001…SEC-009) — replaces `docs/superpowers` agent artifacts.

## 1) Full Verification (automated gates)

```bash
dotnet restore VSHelpDesk.slnx && dotnet build VSHelpDesk.slnx --no-restore -c Release
dotnet test VSHelpDesk.slnx --no-build -c Release --nologo
cd frontend && npm ci && npm run lint && npm test && env -u VITE_API_BASE_URL npm run build:budget
kubectl kustomize deploy/k8s/base >/dev/null && kubectl kustomize deploy/k8s/overlays/prod >/dev/null
```

Expected: all green; `npm run build:budget` ≤ 120 KiB JS gzip / 15 KiB CSS gzip (see `frontend/scripts/check-bundle-budget.mjs`). CI additionally runs `npm audit --audit-level=moderate` (allow `react-router` GHSA exception only), `Trivy` HIGH/CRITICAL, `gitleaks`, `dotnet list package --vulnerable`.

Optional full e2e:

```bash
cd frontend && npx playwright test   # 4 viewport projects (Weeks 2–4)
```

## 2) JWT Lifetime & Revocation (SEC-001)

- [ ] `src/VSHelpDesk.WebAPI/appsettings.json:Auth:ExpirationMinutes` is `60`.
- [ ] `AuthOptionsValidator` rejects `<15` or `>60` (e.g. `Auth__ExpirationMinutes=480` → startup `InvalidOperationException`).
- [ ] Login → capture `vshd.auth`; change role / deactivate / reset password as Admin → old cookie `GET /api/auth/me` = 401, new login works, `UserAuditEvents` row exists, token lifetime ≈60m (`exp - iat`).

## 3) Mail Trust Boundary (SEC-002)

- [ ] No `Authentication-Results` or `dmarc=fail` inbound → reply that would append to existing ticket is **quarantined** (new ticket not appended), `processingNote` contains `Sender authentication failed (DMARC)` (check `InboundEmailNormalizerTests` + `AppendCustomerReplyHandler` evidence).
- [ ] With `Authentication-Results: dmarc=pass` and matching customer address → reply appends / reopens as before.
- [ ] Operator runbook: gateway injects `Authentication-Results: dmarc=pass` for customer domain and strips client-supplied `Authentication-Results` (documented in `docs/deploy-production.md` § Inbound Mail — MTA Authentication & Quotas).

## 4) IMAP Quotas (SEC-003)

- [ ] `MailboxQuota` (single source: `VSHelpDesk.Domain.Mail.MailboxQuota`): `MaxMessagesPerRun=100`, `MaxAttachmentsPerMessage=10`, `MaxAggregateBytesPerRun=52428800` (50 MiB), `MaxRawMessageBytes=5242880` (5 MiB) — `MailboxQuotaOptions` defaults to same, `InboundMailLimits` no longer duplicates.
- [ ] Ingest 500 unseen mails → handler processes ≤100, remainder deferred to next run; `aggregate-quota-exceeded` durable quarantine (DB record before Seen) for overshoot.
- [ ] Message with 15 attachments → quarantined `Too many attachments: 15 exceeds limit 10` (count checked before decoding, durable quarantine).
- [ ] Raw message >5 MiB → durable quarantine (`RawSize` check in normalizer) before Seen, no infinite re-fetch loop.

## 5) Proxy & Rate Limit (SEC-005)

- [ ] `ForwardedHeaders:ForwardLimit=2` (edge → web nginx → API); setting `0` or `11` → startup throw.
- [ ] `frontend/nginx.conf` + `deploy/k8s/base/web-nginx-configmap.yaml` map preserves `X-Forwarded-Proto` (`$forwarded_proto`), not `$scheme`.
- [ ] `GET /__test/remote-ip` (Development) with `X-Forwarded-For: 203.0.113.7, 10.0.0.5` (trusted) returns `203.0.113.7` and `https` when `X-Forwarded-Proto: https`; with untrusted CIDR returns direct `RemoteIpAddress`.
- [ ] `auth-login` rate limit partitions on sanitized `ip` (and `ip:username` when `X-Login-Username` sent); `X-Login-Username` normalized `Trim+ToLower`.

## 6) Supply Chain (SEC-007)

- [ ] `frontend/Dockerfile` uses `nginx:1.28-alpine@sha256:…` (no `1.27` remains).
- [ ] `deploy/k8s/base/cronjob-*.yaml` uses `curlimages/curl:8.13.0@sha256:…` (no `8.5.0`).
- [ ] `docker build -f Dockerfile -t test-api .` and `docker build -f frontend/Dockerfile -t test-web ./frontend` succeed; `kubectl kustomize …` still passes.

## 7) CSP & Headers (SEC-008)

- [ ] `curl -I http://localhost:8080/` (web) returns `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'` and `Permissions-Policy: camera=(), microphone=(), geolocation=()` and `X-Content-Type-Options: nosniff` and `X-Frame-Options: DENY` with `always` (also on error responses).
- [ ] No inline script; `style-src 'unsafe-inline'` retained only for Vite-injected styles (documented).

## 8) Audit (SEC-009)

- [ ] `UserAuditEvents` table exists with indexes `IX_UserAuditEvents_TargetUserId_CreatedAt` + `IX_UserAuditEvents_ActorUserId`.
- [ ] Admin create user → audit row `EventType=Created` (`ActorUserId`=admin, `TargetUserId`=new user).
- [ ] Admin change role → `RoleChanged` with `BeforeRole/AfterRole`; toggle active → `ActiveChanged` with `BeforeIsActive/AfterIsActive`; reset password → `PasswordReset` (no hash/secret stored).

## 9) Attachments (SEC-006)

- [ ] `application/msword` upload → `400 attachment-type-not-allowed`; `shell.exe` / unknown extension → rejected.
- [ ] `.docx` that is a ZIP containing `word/vbaProject.bin` → `400 attachment-macro-rejected` (OOXML macro guard).
- [ ] Valid `report.pdf` / `image.png` → `201` with `ScanVerdict=Unscanned` + warning `"Attachment has not been virus-scanned."`.
- [ ] Filename `>255` or chars outside `a-zA-Z0-9._\- ` → validation error.

## 10) Performance Budget

- [ ] Repeat `performance/README.md` runbook or CI `ticket-read-performance.json` shows p95 < 2000 ms overall, p99 < 3000 ms, 0% errors under 20 VU baseline — matches `docs/performance-evidence/ticket-read-baseline.md` verdict.

## Sign-off

Date / commit / verifier: __________________  `git rev-parse HEAD`: `________`
