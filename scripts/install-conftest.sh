#!/usr/bin/env bash
set -euo pipefail

# Source checksum: https://github.com/open-policy-agent/conftest/releases/download/v0.69.0/checksums.txt
CONFTEST_VERSION="0.69.0"
CONFTEST_SHA256="96fc2fbf11f0afde51256647127e6f00a64ce839a4d9a0a1aef2426c0e6f4b3f"
CONFTEST_ARCHIVE="conftest_${CONFTEST_VERSION}_Linux_x86_64.tar.gz"
CONFTEST_URL="https://github.com/open-policy-agent/conftest/releases/download/v${CONFTEST_VERSION}/${CONFTEST_ARCHIVE}"
DESTINATION="${1:-.tools/conftest}"

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "ERROR: pinned Conftest artifact supports Linux x86_64 only" >&2
  exit 1
fi

mkdir -p "$(dirname "$DESTINATION")"
TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT

curl --fail --location --silent --show-error --retry 2 --output "$TEMP_DIR/$CONFTEST_ARCHIVE" "$CONFTEST_URL"
printf '%s  %s\n' "$CONFTEST_SHA256" "$TEMP_DIR/$CONFTEST_ARCHIVE" | sha256sum --check --status
tar -xzf "$TEMP_DIR/$CONFTEST_ARCHIVE" -C "$TEMP_DIR" conftest
install -m 0755 "$TEMP_DIR/conftest" "$DESTINATION"
"$DESTINATION" --version
echo "Conftest v${CONFTEST_VERSION} installed with verified SHA-256."
