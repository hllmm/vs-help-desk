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
| `JOBS_API_KEY` | Min 16 random characters | API key for background scheduled job endpoints |
| `ConnectionStrings__DefaultConnection` | Valid PostgreSQL URI | Primary database connection string |
| `Cors__AllowedOrigins__0` | HTTPS Portal Domain | Allowed frontend origin for CORS |

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
