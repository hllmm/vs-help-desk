#!/usr/bin/env bash
set -euo pipefail

# check-prod-manifest.sh — reject mutable tags and require immutable refs
# Usage: bash scripts/check-prod-manifest.sh /tmp/prod.yaml
#   or: kubectl kustomize deploy/k8s/overlays/prod | bash scripts/check-prod-manifest.sh

INPUT="${1:-/dev/stdin}"

if [[ "$INPUT" != "/dev/stdin" && ! -f "$INPUT" ]]; then
  echo "ERROR: manifest file not found: $INPUT" >&2
  exit 1
fi

CONTENT="$(cat "$INPUT")"

# 1. Reject any :latest or :local tag (mutable)
if echo "$CONTENT" | grep -Eq 'image:[[:space:]]*[^[:space:]"'\'']*:(latest|local)([[:space:]"'\'']|$)'; then
  echo "ERROR: production manifest contains mutable image tag :latest or :local" >&2
  echo "       All production images must be immutable (@sha256: or :sha-<40>)" >&2
  # Show offending lines for debugging
  echo "$CONTENT" | grep -E 'image:[[:space:]]*[^[:space:]]*:(latest|local)' | head -n 20 >&2 || true
  exit 1
fi

# 2. Require at least one immutable reference for api/web (either @sha256:64hex or :sha-40hex)
#    This ensures prod was rendered via render-prod-manifest.sh or has a pinned digest.
if ! echo "$CONTENT" | grep -Eq 'image:[[:space:]]*[^[:space:]]+(@sha256:[a-f0-9]{64}|:sha-[a-f0-9]{40})([[:space:]"'\'']|$)'; then
  echo "ERROR: production manifest has no immutable image references" >&2
  echo "       Expected api/web images as repo@sha256:<64> or repo:sha-<40>" >&2
  exit 1
fi

# 3. Specifically check that api and web are not still using bare local/latest without digest
#    (already covered by #1, but be explicit)
if echo "$CONTENT" | grep -Eq 'image:[[:space:]]*vshelpdesk-(api|web):'; then
  echo "ERROR: production manifest still contains vshelpdesk-api/web with mutable tag" >&2
  exit 1
fi

echo "check-prod-manifest: PASS (no :latest/:local, found immutable refs)"
