#!/usr/bin/env bash
set -euo pipefail

# render-prod-manifest.sh — generate immutable production manifest
# Usage: API_IMAGE=ghcr.io/org/api@sha256:<64> WEB_IMAGE=ghcr.io/org/web@sha256:<64> bash scripts/render-prod-manifest.sh > production.yaml
#   or:  API_IMAGE=ghcr.io/org/api:sha-<40> WEB_IMAGE=ghcr.io/org/web:sha-<40> bash scripts/render-prod-manifest.sh > production.yaml
#
# Validates that both images are immutable, then renders kustomize and substitutes them.

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

kubectl kustomize deploy/k8s/overlays/prod > "$TMP"

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

cat "$TMP"
