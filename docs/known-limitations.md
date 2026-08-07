# Production Known Limitations

This document records actual production constraints that affect the current deployment model.

## Rate Limiting

- **ASP.NET Core rate limiting is per pod.** The `auth-login` limiter uses an in-memory `FixedWindowRateLimiter` per replica (`login:{ip}`). With multiple API replicas, an attacker can distribute requests across pods and exceed the per-pod limit. Persistent account lockout (see below) provides account-level protection across replicas, but IP-level throttling remains per-pod.
- **Persistent account lockout provides account-level protection across replicas.** Failed-login counters and `LockoutEndUtc` are stored in the `Users` table, so lockout is enforced regardless of which replica handles the request. This complements the per-pod IP limiter.
- **A truly global IP rate limit requires an external gateway or distributed limiter.** For strong global IP throttling, place a gateway (e.g., Ingress/NGINX, Cloudflare, or a Redis-backed rate limiter) in front of the API and configure `ForwardedHeaders:TrustedNetworks` accordingly.

## Data & Availability

- **A single PostgreSQL pod inside the cluster does not provide high availability.** `postgres` is a `StatefulSet` with `replicas: 1` and a single `ReadWriteOnce` PVC. It is not replicated, not backed by an operator, and will be unavailable during node or volume failures. For HA, use an external managed PostgreSQL or a Postgres operator with replication and backups.
- **Attachment durability depends on the configured storage class.** `api-attachments` is a `PersistentVolumeClaim` with `ReadWriteOnce` and `storage: 5Gi`. Durability, backup, and multi-AZ replication depend on the cluster's `StorageClass` (e.g., `gp3`, `ceph`), which is not defined in this repo. Verify that the class provides the required retention and backup for your environment.
- **Portal idempotency records are retained with their tickets.** `PortalTicketRequests` preserves replay semantics and has restrictive ticket/user foreign keys, so rows grow with portal-created tickets and block physical deletion until the idempotency row is handled. Monitor table growth and define a business-approved idempotency retention window before adding hard-delete workflows.

## Quality Gate

- **The real E2E test covers the critical path, not every application workflow.** `frontend/e2e-real/auth-ticket-flow.spec.ts` exercises: `GET /api/auth/csrf` → login via UI → `GET /api/auth/me` session restore → create ticket via UI → open details → logout → protected-route redirect. It does **not** cover every workflow (e.g., every ticket status transition, attachment upload, admin user management, or inbound mail ingestion).

## References

- Rate limiter partitioning: [`src/VSHelpDesk.WebAPI/Program.cs`](../src/VSHelpDesk.WebAPI/Program.cs)
- Lockout domain: [`src/VSHelpDesk.Domain/Entities/User.cs`](../src/VSHelpDesk.Domain/Entities/User.cs)
- NetworkPolicies: [`deploy/k8s/base/networkpolicy-*.yaml`](../deploy/k8s/base/)
- Real E2E: [`frontend/e2e-real/auth-ticket-flow.spec.ts`](../frontend/e2e-real/auth-ticket-flow.spec.ts)
