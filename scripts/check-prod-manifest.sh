#!/usr/bin/env bash
set -euo pipefail

# check-prod-manifest.sh — validate the rendered production manifest structurally
# Usage: bash scripts/check-prod-manifest.sh /tmp/prod.yaml
#   or: kubectl kustomize deploy/k8s/overlays/prod | bash scripts/check-prod-manifest.sh

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFTEST_BIN="${CONFTEST_BIN:-$ROOT_DIR/.tools/conftest}"
POLICY_DIR="$ROOT_DIR/policy/production"
INPUT="${1:-/dev/stdin}"

if [[ "$INPUT" != "/dev/stdin" && ! -f "$INPUT" ]]; then
  echo "ERROR: manifest file not found: $INPUT" >&2
  exit 1
fi

if [[ ! -x "$CONFTEST_BIN" ]]; then
  echo "ERROR: Conftest executable not found: $CONFTEST_BIN" >&2
  exit 1
fi

"$CONFTEST_BIN" test \
  --combine \
  --no-color \
  --parser yaml \
  --policy "$POLICY_DIR" \
  "$INPUT"

echo "check-prod-manifest: PASS (structured production image policy)"
