#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHECKER="$ROOT_DIR/scripts/check-dockerfile-pins.sh"
FIXTURE_DIR="$ROOT_DIR/policy/dockerfile/fixtures"
FAILURES=0

expect_rejected() {
  local fixture="$1"
  local expected_message="$2"
  local output
  local status

  if output="$(bash "$CHECKER" "$fixture" 2>&1)"; then
    status=0
  else
    status=$?
  fi

  if (( status == 0 )); then
    echo "FAIL: expected rejection for ${fixture#$ROOT_DIR/}" >&2
    FAILURES=$((FAILURES + 1))
    return
  fi

  if ! grep -Fq "$expected_message" <<<"$output"; then
    echo "FAIL: ${fixture#$ROOT_DIR/} did not report: $expected_message" >&2
    echo "$output" >&2
    FAILURES=$((FAILURES + 1))
    return
  fi

  echo "EXPECTED REJECT: ${fixture#$ROOT_DIR/}"
}

expect_accepted() {
  local fixture="$1"
  local output
  local status

  if output="$(bash "$CHECKER" "$fixture" 2>&1)"; then
    status=0
  else
    status=$?
  fi

  if (( status != 0 )); then
    echo "FAIL: expected acceptance for ${fixture#$ROOT_DIR/}" >&2
    echo "$output" >&2
    FAILURES=$((FAILURES + 1))
    return
  fi

  echo "EXPECTED ACCEPT: ${fixture#$ROOT_DIR/}"
}

expect_accepted "$FIXTURE_DIR/safe-pinned.Dockerfile"
expect_rejected "$FIXTURE_DIR/unsafe-missing-pin.Dockerfile" \
  "FROM must use a lowercase 64-hex @sha256 digest"
expect_rejected "$FIXTURE_DIR/unsafe-uppercase-pin.Dockerfile" \
  "FROM must use a lowercase 64-hex @sha256 digest"
expect_rejected "$FIXTURE_DIR/unsafe-apk-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-apt-get-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-apt-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-dnf-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-dnf-update.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-yum-update.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-yum-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-zypper-update.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-zypper-upgrade.Dockerfile" \
  "live package upgrade is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-pacman-syu.Dockerfile" \
  "live package upgrade is forbidden"

if (( FAILURES > 0 )); then
  echo "Dockerfile checker fixtures: $FAILURES failure(s)" >&2
  exit 1
fi

echo "Dockerfile checker fixtures: PASS"
