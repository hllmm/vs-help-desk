#!/usr/bin/env bash
set -euo pipefail

# check-k8s-security.sh — run the NetworkPolicy acceptance suite and validate
# a rendered manifest against the same fail-closed policy.
# Usage: bash scripts/check-k8s-security.sh /path/to/rendered-manifest.yaml

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFTEST_BIN="${CONFTEST_BIN:-$ROOT_DIR/.tools/conftest}"
POLICY_DIR="$ROOT_DIR/policy/networkpolicy"
INPUT="${1:-}"

if [[ -z "$INPUT" || ! -f "$INPUT" ]]; then
  echo "ERROR: provide an existing rendered manifest file" >&2
  exit 1
fi

if [[ ! -x "$CONFTEST_BIN" ]]; then
  echo "ERROR: Conftest executable not found: $CONFTEST_BIN" >&2
  exit 1
fi

# Keep the fixture suite as part of this entry point so acceptance cannot pass
# if a policy regression weakens a check that the current manifest happens not
# to exercise.
bash "$ROOT_DIR/scripts/check-networkpolicy-policy.sh"

"$CONFTEST_BIN" test \
  --combine \
  --no-color \
  --parser yaml \
  --policy "$POLICY_DIR" \
  "$INPUT"

echo "check-k8s-security: PASS (NetworkPolicy policy)"
