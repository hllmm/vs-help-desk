#!/usr/bin/env bash
set -euo pipefail

# Runs NetworkPolicy policy fixtures and requires the rendered base to pass.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFTEST_BIN="${CONFTEST_BIN:-$ROOT_DIR/.tools/conftest}"
POLICY_DIR="$ROOT_DIR/policy/networkpolicy"
FIXTURE_DIR="$POLICY_DIR/fixtures"

if [[ ! -x "$CONFTEST_BIN" ]]; then
  echo "ERROR: Conftest executable not found: $CONFTEST_BIN" >&2
  exit 1
fi

expect_rejected() {
  local input_file="$1"
  shift
  local output

  if output="$("$CONFTEST_BIN" test --policy "$POLICY_DIR" "$input_file" 2>&1)"; then
    echo "ERROR: expected policy rejection for ${input_file#$ROOT_DIR/}" >&2
    echo "$output" >&2
    exit 1
  fi

  for expected_message in "$@"; do
    if ! grep -Fq "$expected_message" <<<"$output"; then
      echo "ERROR: rejection for ${input_file#$ROOT_DIR/} did not contain: $expected_message" >&2
      echo "$output" >&2
      exit 1
    fi
  done

  echo "EXPECTED REJECT: ${input_file#$ROOT_DIR/}"
}

expect_accepted() {
  local input_file="$1"
  local output

  if output="$("$CONFTEST_BIN" test --policy "$POLICY_DIR" "$input_file" 2>&1)"; then
    echo "EXPECTED ACCEPT: ${input_file#$ROOT_DIR/}"
    return 0
  fi

  echo "ERROR: expected policy acceptance for ${input_file#$ROOT_DIR/}" >&2
  echo "$output" >&2
  return 1
}

expect_rejected "$FIXTURE_DIR/unsafe-world-ipv4.yaml" "unrestricted ipBlock CIDR 0.0.0.0/0 is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-world-ipv6.yaml" "unrestricted ipBlock CIDR ::/0 is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-world-ipv6-expanded.yaml" "unrestricted ipBlock CIDR 0:0:0:0:0:0:0:0/0 is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-web-or-selectors.yaml" "web ingress must combine namespaceSelector and podSelector in one peer"
expect_rejected "$FIXTURE_DIR/unsafe-web-or-selectors-separate-entries.yaml" "web ingress must combine namespaceSelector and podSelector in one peer"
expect_rejected "$FIXTURE_DIR/unsafe-base-mail-cidr.yaml" "SMTP/IMAP egress with an ipBlock is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-renamed-mail-policy.yaml" "SMTP/IMAP egress with an ipBlock is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-mail-port-range.yaml" "SMTP/IMAP egress with an ipBlock is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-placeholder-relay-cidr.yaml" "example relay CIDR 10.20.30.0/24 is forbidden"
expect_rejected "$FIXTURE_DIR/unsafe-placeholder-relay-ip.yaml" "example relay CIDR 192.168.100.10/32 is forbidden"

RENDERED_BASE="$(mktemp --suffix=.yaml)"
trap 'rm -f "$RENDERED_BASE"' EXIT
kubectl kustomize "$ROOT_DIR/deploy/k8s/base" >"$RENDERED_BASE"
expect_accepted "$RENDERED_BASE"

"$CONFTEST_BIN" test --policy "$POLICY_DIR" "$FIXTURE_DIR/safe-required-configuration.yaml"
"$CONFTEST_BIN" test --policy "$POLICY_DIR" "$FIXTURE_DIR/safe-unrelated-tcp-cidr.yaml"
echo "NetworkPolicy policy fixtures: PASS"
