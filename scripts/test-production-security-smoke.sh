#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SMOKE_SCRIPT="$ROOT_DIR/scripts/run-production-security-smoke.sh"
WORKFLOW="$ROOT_DIR/.github/workflows/ci.yml"
NGINX_IMAGE='nginxinc/nginx-unprivileged:1.30-alpine@sha256:44e36330f74d4f3a1d4e222acca9e23b401fb87811a7597024502bb759c4dd49'

test -x "$SMOKE_SCRIPT"

grep -Fq 'ASPNETCORE_ENVIRONMENT=Production' "$SMOKE_SCRIPT"
grep -Fq 'openssl' "$SMOKE_SCRIPT"
grep -Fq 'server.pfx' "$SMOKE_SCRIPT"
grep -Fq "$NGINX_IMAGE" "$SMOKE_SCRIPT"
grep -Fq 'docker run' "$SMOKE_SCRIPT"
grep -Fq 'postgres:16-alpine@sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777' "$SMOKE_SCRIPT"
grep -Fq 'Secure' "$SMOKE_SCRIPT"
grep -Fq 'SameSite=Lax' "$SMOKE_SCRIPT"
grep -Fq '/api/auth/me' "$SMOKE_SCRIPT"
grep -Fq '429' "$SMOKE_SCRIPT"
grep -Fq 'X-Login-Username' "$SMOKE_SCRIPT"
grep -Fq 'X-Forwarded-Proto' "$SMOKE_SCRIPT"
grep -Fq 'proxy_set_header X-Forwarded-For $remote_addr;' "$SMOKE_SCRIPT"
grep -Fq 'EDGE_CLIENT_ADDRESS=127.0.0.3' "$SMOKE_SCRIPT"
grep -Fq 'ForwardedHeaders__TrustedNetworks__1=' "$SMOKE_SCRIPT"
if grep -Fq 'proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;' "$SMOKE_SCRIPT"; then
  echo 'client-supplied X-Forwarded-For must not be forwarded by the edge' >&2
  exit 1
fi

grep -Fq 'production-security-smoke:' "$WORKFLOW"
grep -Fq 'bash scripts/run-production-security-smoke.sh' "$WORKFLOW"

# The functional browser E2E must remain Development-only and separate.
grep -A80 '^  e2e-real:' "$WORKFLOW" | grep -Fq 'ASPNETCORE_ENVIRONMENT=Development'

echo 'Production TLS smoke policy: PASS'
