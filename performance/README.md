# Ticket read-path performance evidence

Dependency-free tooling that proves the bounded ticket read path against a
production-shaped PostgreSQL fixture:

| File | Purpose |
| ---- | ------- |
| `seed-ticket-read.sql` | Guarded fixture: 100,000 tickets, 500,000 messages, 100 deterministic users, 50,000 attachment metadata rows. |
| `ticket-read-load.mjs` | Node.js built-ins-only 20-VU HTTP load runner with p50/p95/p99 + error-rate SLO gates. |
| `docker-compose.performance.yml` | Override that binds the production Compose stack to loopback with database `VS_HelpDesk_Perf`. |
| `sample-compose-stats.sh` | 1 Hz `docker stats` sampler (newline-delimited JSON) for CPU/memory evidence. |
| `../frontend/scripts/check-bundle-budget.mjs` | Production bundle budget gate (JS ≤ 120 KiB gzip, CSS ≤ 15 KiB gzip). |

## Fixture safety

`seed-ticket-read.sql` begins with `\set ON_ERROR_STOP on` and refuses to run
outside a database literally named `VS_HelpDesk_Perf`. Inside one transaction it
acquires advisory lock `86020802`, truncates only ticket/message/processed-mail/
attachment tables, upserts 100 synthetic users, inserts the deterministic
tickets/messages/attachments, `ANALYZE`s, asserts final counts, and commits.
The database name cannot be overridden.

`perf-admin` / `local-perf-password` exists only because its ASP.NET Identity
hash is a committed **local fixture value**. It must never be copied into any
company, staging, or production configuration. The same applies to the
`performance/.env` values — the file is git-ignored and loopback-only.

## Reproduce the evidence locally

Prerequisites: Docker, .NET 10 SDK with `dotnet-ef`, Node.js ≥ 22, and a private
`performance/.env` (git-ignored). Shape:

```bash
cat > performance/.env <<'EOF'
POSTGRES_USER=vs_help_desk
POSTGRES_PASSWORD=local-perf-only
POSTGRES_DB=VS_HelpDesk_Perf
AUTH_SIGNING_KEY=local-perf-signing-key-32-bytes-min
JOBS_API_KEY=local-perf-jobs-key-16
# IMAP/SMTP point at dead loopback ports; the read-path load run never touches
# mail (production validation rejects ReceiverMode=Fake outside Dev/Testing).
EMAIL_RECEIVER_MODE=Imap
EMAIL_SMTP_HOST=127.0.0.1
EMAIL_SMTP_PORT=2525
EMAIL_SMTP_SECURITY=StartTls
EMAIL_SMTP_USERNAME=
EMAIL_SMTP_PASSWORD=
EMAIL_IMAP_HOST=127.0.0.1
EMAIL_IMAP_PORT=3143
EMAIL_IMAP_SECURITY=SslOnConnect
EMAIL_IMAP_USERNAME=perf-unused
EMAIL_IMAP_PASSWORD=local-perf-only-not-a-real-mailbox
EMAIL_IMAP_ACCOUNT_ID=perf-unused
EMAIL_IMAP_FOLDER=INBOX
EOF
```

1. Start the database and apply migrations, then seed (takes a few minutes):

   ```bash
   docker compose \
     --env-file performance/.env \
     -f docker-compose.prod.yml \
     -f performance/docker-compose.performance.yml \
     up -d --build db
   dotnet ef database update \
     --project src/VSHelpDesk.Infrastructure/VSHelpDesk.Infrastructure.csproj \
     --startup-project src/VSHelpDesk.WebAPI/VSHelpDesk.WebAPI.csproj \
     --context ApplicationDbContext \
     --connection 'Host=127.0.0.1;Port=55432;Database=VS_HelpDesk_Perf;Username=vs_help_desk;Password=local-perf-only'
   PGPASSWORD='local-perf-only' psql \
     --host 127.0.0.1 --port 55432 --username vs_help_desk \
     --dbname VS_HelpDesk_Perf --file performance/seed-ticket-read.sql
   ```

   Without a host `psql`, run it inside the container instead:

   ```bash
   cat performance/seed-ticket-read.sql | docker exec -i vshelpdesk-perf-db-1 \
     psql -U vs_help_desk -d VS_HelpDesk_Perf -f -
   ```

2. Start API + web:

   ```bash
   docker compose \
     --env-file performance/.env \
     -f docker-compose.prod.yml \
     -f performance/docker-compose.performance.yml \
     up -d --build api web
   ```

3. Sample container resources in a second terminal (Ctrl+C to stop):

   ```bash
   bash performance/sample-compose-stats.sh performance/compose-stats.ndjson
   ```

4. Run the load through nginx (logins are paced to respect the production
   login rate limit; 20 logins take ~2 minutes before the warm-up starts):

   ```bash
   PERF_USERNAME='perf-admin' \
   PERF_PASSWORD='local-perf-password' \
   PERF_BASE_URL='http://127.0.0.1:18080' \
   PERF_VUS=20 \
   PERF_DURATION_SEC=60 \
   PERF_WARMUP_SEC=15 \
   node performance/ticket-read-load.mjs
   ```

   Exit code is non-zero when there are no measured samples, when p95 ≥
   2000 ms, p99 ≥ 3000 ms, or the error rate ≥ 1%. The final `PERF_RESULT {...}`
   line is the machine-readable evidence artifact.

## Bundle budget

```bash
cd frontend
node --test scripts/check-bundle-budget.test.mjs   # checker unit tests
npm run build:budget                                # build + enforce budgets
```

## Teardown

```bash
docker compose \
  --env-file performance/.env \
  -f docker-compose.prod.yml \
  -f performance/docker-compose.performance.yml \
  down -v
```

`down -v` removes only this project's isolated volume
(`vshelpdesk-perf_*`); it never touches other stacks.

The full 100k-ticket run is **not** part of per-commit CI. It is reproducible
here and via the manual `workflow_dispatch` (`run_performance=true`) CI gate;
measured results live in `docs/performance-evidence/ticket-read-baseline.md`.
