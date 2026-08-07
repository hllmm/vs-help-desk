#!/usr/bin/env bash
set -euo pipefail

# Runs NetworkPolicy policy fixtures. Current base manifests are an expected
# rejection until Task 6 removes their known unsafe rules.

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
expect_rejected "$ROOT_DIR/deploy/k8s/base" \
  "unrestricted ipBlock CIDR 0.0.0.0/0 is forbidden" \
  "web ingress must combine namespaceSelector and podSelector in one peer" \
  "SMTP/IMAP egress with an ipBlock is forbidden" \
  "example relay CIDR 10.20.30.0/24 is forbidden" \
  "example relay CIDR 192.168.100.10/32 is forbidden"

"$CONFTEST_BIN" test --policy "$POLICY_DIR" "$FIXTURE_DIR/safe-required-configuration.yaml"
"$CONFTEST_BIN" test --policy "$POLICY_DIR" "$FIXTURE_DIR/safe-unrelated-tcp-cidr.yaml"
echo "NetworkPolicy policy fixtures: PASS"
