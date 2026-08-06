# VSHelpDesk Production Deployment Guide

## Overview
This document outlines production deployment procedures, environment variables, security configurations, and infrastructure requirements for `vs-help-desk`.

## Prerequisites
- Docker / Kubernetes Cluster
- PostgreSQL 16+ Database
- Nginx / Ingress Controller with TLS Termination
- Outbound SMTP and Inbound IMAP Mail Server Access

## Production Environment Variables (`.env` / K8s Secrets)

| Variable | Requirement | Description |
|----------|-------------|-------------|
| `AUTH_SIGNING_KEY` | Min 32 random UTF-8 bytes | JWT signing key (Must NOT use committed placeholders) |
| `Auth__ExpirationMinutes` | 15–60 (default 60) | JWT lifetime; enforced by `AuthOptionsValidator` (outside range → startup fail) |
| `JOBS_API_KEY` | Min 16 random characters | API key for background scheduled job endpoints |
| `ConnectionStrings__DefaultConnection` | Valid PostgreSQL URI | Primary database connection string |
| `Cors__AllowedOrigins__0` | HTTPS Portal Domain | Allowed frontend origin for CORS |
| `ForwardedHeaders__ForwardLimit` | 1–10 (default 2) | Proxy hops trusted; edge/Ingress → web nginx → API |
| `ForwardedHeaders__TrustedNetworks__*` | CIDR list | Only Ingress/edge + web nginx CIDRs (see Forwarded Headers section) |

## Forwarded Headers & Rate Limiting
The application models a 2-hop proxy chain: `edge/Ingress -> web nginx -> API`. `ForwardedHeadersOptions` is loaded from `ForwardedHeaders:TrustedNetworks` and `ForwardedHeaders:ForwardLimit` (default `2`).

```json
"ForwardedHeaders": {
  "ForwardLimit": 2,
  "TrustedNetworks": [
    "10.20.30.0/24"
  ]
}
```

- `ForwardLimit = 2` prevents attacker-supplied `X-Forwarded-For` entries from being trusted beyond the two known proxies. Override via env var `ForwardedHeaders__ForwardLimit=2`. Valid range is `1-10`; values outside this range cause startup failure (`InvalidOperationException`) — clamp intentionally not applied to surface misconfiguration.
- `KnownIPNetworks` / `KnownProxies` are cleared and populated only from `TrustedNetworks`. In production, override `ForwardedHeaders:TrustedNetworks` in `appsettings.Production.json` or environment variables to include only your specific Ingress/edge and web nginx CIDRs (e.g., `10.20.30.0/24`). **Array merge caveat:** `TrustedNetworks` binds via indexed keys (`ForwardedHeaders:TrustedNetworks:0`). If you override via `ForwardedHeaders__TrustedNetworks__0` with fewer CIDRs than `appsettings.json`, stale indices from the JSON (e.g., `:1`) survive the merge. Prefer overriding the whole array in `appsettings.Production.json` or setting all indices explicitly (`:0`, `:1`, …) to avoid leftover defaults.
- `RequireHeaderSymmetry = false` — `X-Forwarded-For` and `X-Forwarded-Proto` are evaluated independently; proto preservation is handled at the web nginx layer.
- Web nginx (`frontend/nginx.conf` and `deploy/k8s/base/web-nginx-configmap.yaml`) preserves the original `X-Forwarded-Proto` from edge using `map $http_x_forwarded_proto $forwarded_proto { default $scheme; "~.+" $http_x_forwarded_proto; }` and `proxy_set_header X-Forwarded-Proto $forwarded_proto;` (not overwriting with `$scheme`). This keeps `Request.Scheme` correct (https) when TLS terminates at edge.
- Rate limiter `auth-login` uses `HttpContext.Connection.RemoteIpAddress` **after** `UseForwardedHeaders` sanitization, so `X-Forwarded-For` is only honored for trusted networks/limit. Partition key is `login:{ip}` or `login:{ip}:{normalized-username}` where `X-Login-Username` is `Trim().ToLowerInvariant()` and not trusted for auth.

Ensure your edge/Ingress sets `X-Forwarded-For` (client IP appended) and `X-Forwarded-Proto` (original scheme).

## Auth & Session

- JWT lifetime `Auth:ExpirationMinutes` default **60**, allowed **15–60** (validator fail-loud). Override via `Auth__ExpirationMinutes`.
- `SecurityStamp` revocation: role change, `IsActive` toggle, or password reset rotates `User.SecurityStamp` and the next `OnTokenValidated` rejects the old `security_stamp` claim (no grace period).

## Inbound Mail — MTA Authentication & Quotas

- **MTA requirement:** production mail gateway must append `Authentication-Results: … dmarc=pass …` for the customer domain and strip any client-supplied `Authentication-Results`. `ImapEmailReceiver` → `EmailAuthenticationResultParser` → `InboundEmailNormalizer` quarantines replies whose target ticket exists but DMARC is not aligned (`Sender authentication failed (DMARC)`).
- **Quotas (`InboundMailLimits`):** `MaxMessagesPerRun=100`, `MaxAttachmentsPerMessage=10`, `MaxAggregateBytesPerRun=50 MiB`, `MaxRawMessageBytes=5 MiB` per raw message. `FetchUnreadAsync` caps UID batch; handler streams with aggregate budget (remaining mails get `aggregate-quota-exceeded` quarantine + `processingNote`). `Too many attachments…` quarantine for per-message overflow.

## Content Security Policy & Attachment Policy

- **CSP** enforced at nginx edge (`frontend/nginx.conf` and `deploy/k8s/base/web-nginx-configmap.yaml`, `always`): `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'` plus `Permissions-Policy`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`.
- **Attachments:** allowlist `pdf/png/jpeg/gif/webp/txt/docx/xlsx` only; `application/msword` rejected; OOXML ZIP central-directory scanned for `vbaProject.bin` macro → `attachment-macro-rejected`; `ScanVerdict` stored (`Unscanned` default, surfaced with warning). Filename `≤255`, `^[a-zA-Z0-9._\- ]+$`.

## Audit

- `UserAuditEvents` (append-only, `IX_UserAuditEvents_TargetUserId_CreatedAt` / `IX_UserAuditEvents_ActorUserId`): `CreateUser` / `UpdateUser` (role + active transitions) / `SetUserPassword` write rows with `ActorUserId`, `TargetUserId`, `EventType`, `Before/After` snapshots, `CorrelationId`. Secrets never logged.

## Deployment Steps
1. Apply Database Migrations:
   ```bash
   dotnet ef database update --project src/VSHelpDesk.Infrastructure --startup-project src/VSHelpDesk.WebAPI
   ```
2. Build Production WebAPI Container:
   ```bash
   docker build -t vs-help-desk-api:latest -f Dockerfile .
   ```
3. Build Production Frontend SPA Assets:
   ```bash
   cd frontend && npm run build
   ```
4. Deploy containers via Helm or Kubernetes manifests in `deploy/k8s/`.
