#!/usr/bin/env bash
set -euo pipefail

# render-prod-manifest.sh — generate immutable production manifest
# Usage: API_IMAGE=... WEB_IMAGE=... MAIL_EGRESS_MODE=disabled bash scripts/render-prod-manifest.sh > production.yaml
#   or:  API_IMAGE=... WEB_IMAGE=... MAIL_EGRESS_MODE=enabled SMTP_RELAY_CIDRS=... IMAP_RELAY_CIDRS=... bash scripts/render-prod-manifest.sh > production.yaml
#
# Validates the mail egress contract and immutable images, then renders kustomize and substitutes them.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -z "${MAIL_EGRESS_MODE:-}" ]]; then
  echo "ERROR: MAIL_EGRESS_MODE must be set to enabled or disabled" >&2
  exit 1
fi

case "$MAIL_EGRESS_MODE" in
  disabled)
    ;;
  enabled)
    if [[ -z "${SMTP_RELAY_CIDRS:-}" ]]; then
      echo "ERROR: SMTP_RELAY_CIDRS must be set when MAIL_EGRESS_MODE=enabled" >&2
      exit 1
    fi
    if [[ -z "${IMAP_RELAY_CIDRS:-}" ]]; then
      echo "ERROR: IMAP_RELAY_CIDRS must be set when MAIL_EGRESS_MODE=enabled" >&2
      exit 1
    fi
    ;;
  *)
    echo "ERROR: MAIL_EGRESS_MODE must be exactly enabled or disabled, got: $MAIL_EGRESS_MODE" >&2
    exit 1
    ;;
esac

if [[ -z "${API_IMAGE:-}" ]]; then
  echo "ERROR: API_IMAGE must be set to ghcr.io/vs-help-desk/api@sha256:<64 lowercase hex>" >&2
  exit 1
fi
if [[ -z "${WEB_IMAGE:-}" ]]; then
  echo "ERROR: WEB_IMAGE must be set to ghcr.io/vs-help-desk/web@sha256:<64 lowercase hex>" >&2
  exit 1
fi

API_IMAGE_REGEX='^ghcr\.io/vs-help-desk/api@sha256:[a-f0-9]{64}$'
WEB_IMAGE_REGEX='^ghcr\.io/vs-help-desk/web@sha256:[a-f0-9]{64}$'

if ! [[ "$API_IMAGE" =~ $API_IMAGE_REGEX ]]; then
  echo "ERROR: API_IMAGE must use the exact allow-listed repository and a sha256 digest, got: $API_IMAGE" >&2
  exit 1
fi
if ! [[ "$WEB_IMAGE" =~ $WEB_IMAGE_REGEX ]]; then
  echo "ERROR: WEB_IMAGE must use the exact allow-listed repository and a sha256 digest, got: $WEB_IMAGE" >&2
  exit 1
fi

if [[ "$API_IMAGE" =~ @sha256:a{64}$ || "$API_IMAGE" =~ @sha256:b{64}$ ]]; then
  echo "ERROR: API_IMAGE uses a prohibited all-a/all-b placeholder digest" >&2
  exit 1
fi
if [[ "$WEB_IMAGE" =~ @sha256:a{64}$ || "$WEB_IMAGE" =~ @sha256:b{64}$ ]]; then
  echo "ERROR: WEB_IMAGE uses a prohibited all-a/all-b placeholder digest" >&2
  exit 1
fi

# Render base prod kustomization
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

if ! command -v kubectl >/dev/null 2>&1; then
  echo "ERROR: kubectl not found in PATH" >&2
  exit 1
fi

MAIL_EGRESS_POLICY=""
if [[ "$MAIL_EGRESS_MODE" == "enabled" ]]; then
  if ! MAIL_EGRESS_POLICY="$(python3 "$ROOT_DIR/scripts/generate_mail_egress_policy.py" \
    --smtp-relay-cidrs "$SMTP_RELAY_CIDRS" \
    --imap-relay-cidrs "$IMAP_RELAY_CIDRS")"; then
    exit 1
  fi
fi

kubectl kustomize "$ROOT_DIR/deploy/k8s/overlays/prod" > "$TMP"

# Replace only the known base local image references. If either reference is
# absent, the structured validator below rejects the rendered artifact.
sed -i -E "s|vshelpdesk-api:local|$API_IMAGE|g" "$TMP"
sed -i -E "s|vshelpdesk-web:local|$WEB_IMAGE|g" "$TMP"

if [[ "$MAIL_EGRESS_MODE" == "enabled" ]]; then
  printf '%s\n' "$MAIL_EGRESS_POLICY" >> "$TMP"
fi

# Keep stdout as a deployable YAML stream. Validator diagnostics go to stderr.
if ! bash "$ROOT_DIR/scripts/check-prod-manifest.sh" "$TMP" >&2; then
  echo "ERROR: rendered production manifest failed structured image validation" >&2
  exit 1
fi

cat "$TMP"
