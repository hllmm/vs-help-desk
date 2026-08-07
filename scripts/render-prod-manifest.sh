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
  echo "ERROR: API_IMAGE must be set (e.g., ghcr.io/vs-help-desk/api@sha256:<64> or :sha-<40>)" >&2
  exit 1
fi
if [[ -z "${WEB_IMAGE:-}" ]]; then
  echo "ERROR: WEB_IMAGE must be set (e.g., ghcr.io/vs-help-desk/web@sha256:<64> or :sha-<40>)" >&2
  exit 1
fi

IMMUTABLE_REGEX='^[^[:space:]]+(@sha256:[a-f0-9]{64}|:sha-[a-f0-9]{40})$'

if ! [[ "$API_IMAGE" =~ $IMMUTABLE_REGEX ]]; then
  echo "ERROR: API_IMAGE must be immutable (repo@sha256:<64 hex> or repo:sha-<40 hex>), got: $API_IMAGE" >&2
  exit 1
fi
if ! [[ "$WEB_IMAGE" =~ $IMMUTABLE_REGEX ]]; then
  echo "ERROR: WEB_IMAGE must be immutable (repo@sha256:<64 hex> or repo:sha-<40 hex>), got: $WEB_IMAGE" >&2
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

# Replace the placeholder prod images with the provided immutable ones.
# Prod kustomization uses vshelpdesk-api:sha-... / vshelpdesk-web:sha-... placeholders; replace the whole image line.
# Use a delimiter not in image names.

# Replace api
# Match: image: <anything vshelpdesk-api ...>  -> image: $API_IMAGE
# We handle both with and without digest/tag remnants.
sed -i -E "s|image:[[:space:]]*vshelpdesk-api[^[:space:]]*|image: $API_IMAGE|g" "$TMP"
sed -i -E "s|image:[[:space:]]*ghcr\.io[^[:space:]]*vshelpdesk-api[^[:space:]]*|image: $API_IMAGE|g" "$TMP"

# Replace web
sed -i -E "s|image:[[:space:]]*vshelpdesk-web[^[:space:]]*|image: $WEB_IMAGE|g" "$TMP"
sed -i -E "s|image:[[:space:]]*ghcr\.io[^[:space:]]*vshelpdesk-web[^[:space:]]*|image: $WEB_IMAGE|g" "$TMP"

# Also handle generic ghcr.io/vs-help-desk/api or web if prod already uses ghcr prefix
sed -i -E "s|image:[[:space:]]*ghcr\.io/vs-help-desk/api[^[:space:]]*|image: $API_IMAGE|g" "$TMP"
sed -i -E "s|image:[[:space:]]*ghcr\.io/vs-help-desk/web[^[:space:]]*|image: $WEB_IMAGE|g" "$TMP"

if [[ "$MAIL_EGRESS_MODE" == "enabled" ]]; then
  printf '%s\n' "$MAIL_EGRESS_POLICY" >> "$TMP"
fi

cat "$TMP"
