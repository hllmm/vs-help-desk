#!/usr/bin/env bash
set -euo pipefail

# check-dockerfile-pins.sh — enforce immutable Docker base images and
# deterministic package installation in repository Dockerfiles.
# Usage: bash scripts/check-dockerfile-pins.sh [Dockerfile ...]
# Without arguments, all Dockerfile and Dockerfile.* files below the repository
# root are discovered in bytewise-sorted order. Explicit files are useful for
# focused fixture tests.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly FROM_DIGEST_RE='@sha256:[0-9a-f]{64}([[:space:]]|$)'
readonly LIVE_UPGRADE_RE='(^|[[:space:];|&])(apk([[:space:]]+[^[:space:];|&]+)*[[:space:]]+upgrade|apt(-get)?([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(upgrade|dist-upgrade|full-upgrade)|dnf([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(upgrade|update|system-upgrade)|yum([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(update|upgrade)|zypper([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(update|upgrade|dup)|pacman[[:space:]]+-syu)([[:space:];|&]|$)'

for path in "$@"; do
  if [[ ! -e "$path" ]]; then
    echo "ERROR: Dockerfile path does not exist: $path" >&2
    exit 1
  fi
done

if (( $# == 0 )); then
  mapfile -t DOCKERFILES < <(
    find "$ROOT_DIR" \
      \( -type d \( -name .git -o -name node_modules -o -name bin -o -name obj \) -prune \) -o \
      \( -type f \( -name Dockerfile -o -name 'Dockerfile.*' \) -print \) \
      | LC_ALL=C sort
  )
else
  mapfile -t DOCKERFILES < <(printf '%s\n' "$@" | LC_ALL=C sort -u)
fi

if (( ${#DOCKERFILES[@]} == 0 )); then
  echo "ERROR: no repository Dockerfiles found below $ROOT_DIR" >&2
  exit 1
fi

failures=0
for dockerfile in "${DOCKERFILES[@]}"; do
  if [[ ! -f "$dockerfile" ]]; then
    echo "ERROR: Dockerfile path is not a regular file: $dockerfile" >&2
    failures=$((failures + 1))
    continue
  fi

  display_path="$dockerfile"
  if [[ "$display_path" == "$ROOT_DIR/"* ]]; then
    display_path="${display_path#$ROOT_DIR/}"
  fi

  line_number=0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))

    if [[ "$line" =~ ^[[:space:]]*FROM[[:space:]] ]]; then
      if [[ ! "$line" =~ $FROM_DIGEST_RE ]]; then
        echo "FAIL: $display_path:$line_number: FROM must use a lowercase 64-hex @sha256 digest" >&2
        failures=$((failures + 1))
      fi
    fi

    lower_line="${line,,}"
    if [[ "$lower_line" =~ $LIVE_UPGRADE_RE ]]; then
      echo "FAIL: $display_path:$line_number: live package upgrade is forbidden; install packages in the base image or pin individual packages" >&2
      failures=$((failures + 1))
    fi
  done < "$dockerfile"
done

if (( failures > 0 )); then
  echo "Dockerfile pin check: FAIL ($failures violation(s))" >&2
  exit 1
fi

echo "Dockerfile pin check: PASS (${#DOCKERFILES[@]} Dockerfile(s))"
