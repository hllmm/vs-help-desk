# VSHelpDesk Kubernetes Deployment (Kustomize)

Applies on top of `docs/deploy-production.md`. Images and manifests live under `deploy/k8s/`.

## Base + Overlays

```bash
kubectl kustomize deploy/k8s/base >/dev/null         # smoke
kubectl kustomize deploy/k8s/overlays/prod >/dev/null
kubectl apply -k deploy/k8s/overlays/prod
```

- `deploy/k8s/base`: API + web Deployments, Services, Postgres StatefulSet, `web-nginx-configmap.yaml`, CronJobs (`process-incoming-emails`, `resolve-inactive-tickets` via `curlimages/curl:8.13.0` digest-pinned), Ingress, `secret.example.yaml`.
- `deploy/k8s/overlays/prod`: `ingress-patch.yaml` + image `newName/newTag` rewrites.

## Forwarded Headers & Rate Limiting

Identical to production Compose (see `docs/deploy-production.md` § Forwarded Headers & Rate Limiting):

- `ForwardedHeaders:ForwardLimit=2` (`edge/Ingress → web nginx → API`), 1–10 else startup `InvalidOperationException`.
- `TrustedNetworks` = Ingress + web CIDRs only; array-merge caveat applies (`ForwardedHeaders:TrustedNetworks:0` indexed).
- `RequireHeaderSymmetry=false`; `deploy/k8s/base/web-nginx-configmap.yaml` preserves `$forwarded_proto` via `map $http_x_forwarded_proto $forwarded_proto { default $scheme; "~.+" $http_x_forwarded_proto; }`.
- `auth-login` limiter uses sanitized `RemoteIpAddress`; partition `login:{ip}` / `login:{ip}:{username}`.

## MTA Authentication

Gateway must inject `Authentication-Results: dmarc=pass` for customer domain before delivery to `IMAP`. In-cluster mail path honors the same quarantine as Compose: unauthenticated replies against an existing ticket are quarantined with `Sender authentication failed (DMARC)`.

## CSP & Security Headers

`web-nginx-configmap.yaml` mirrors `frontend/nginx.conf`:

```
add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'" always;
add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
add_header X-Content-Type-Options nosniff always;
add_header X-Frame-Options DENY always;
add_header Referrer-Policy strict-origin-when-cross-origin always;
```

## Quotas, Attachments, Audit, Token Lifetime

Shared with `docs/deploy-production.md`: `InboundMailLimits` (100 msgs/run, 10 attachments/msg, 50 MiB aggregate, 5 MiB raw), extension→MIME allowlist + `vbaProject.bin` macro guard, `ScanVerdict`, `UserAuditEvents` append-only audit, JWT `ExpirationMinutes` 15–60 default 60 with `SecurityStamp` revocation. Set via Kustomize SecretGenerator or env overrides (`Auth__ExpirationMinutes`, `ForwardedHeaders__ForwardLimit`, `IMAP_*`, `SMTP_*`).

## Images & Supply Chain

- `frontend/Dockerfile` → `nginx:1.28-alpine` (digest-pinned).
- `cronjob-*.yaml` → `curlimages/curl:8.13.0` (digest-pinned).
- CI gates: `npm audit --audit-level=moderate`, `Trivy` HIGH/CRITICAL, `gitleaks`, `dotnet list package --vulnerable`.

## Secrets

Never commit `deploy/k8s/**/secret.yaml` (gitignored). Copy `secret.example.yaml` → `secret.yaml` and fill `AUTH_SIGNING_KEY`, `JOBS_API_KEY`, `ConnectionStrings__DefaultConnection`, `Cors__AllowedOrigins__0` via external secret manager or `kubectl create secret`.
