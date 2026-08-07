#!/usr/bin/env bash
set -euo pipefail

# check-dockerfile-pins.sh — enforce immutable Docker base images and
# deterministic package installation in repository Dockerfiles.
# Usage: bash scripts/check-dockerfile-pins.sh [Dockerfile ...]
# Without arguments, all Dockerfile, Dockerfile.*, and *.Dockerfile files
# below the repository root are discovered in bytewise-sorted order. The
# policy fixture directory is intentionally excluded from that scan because it
# contains deliberately unsafe examples; explicit fixture paths are supported
# for focused tests.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURE_DIR="$ROOT_DIR/policy/dockerfile/fixtures"

readonly FROM_IMAGE_RE='^[^[:space:]@]+@sha256:[0-9a-f]{64}$'
readonly APK_UPGRADE_RE='(^|[^[:alnum:]_])apk([[:space:]]+[^[:space:];|&]+)*[[:space:]]+upgrade([^[:alnum:]_]|$)'
readonly APK_ADD_UPGRADE_RE='(^|[^[:alnum:]_])apk([[:space:]]+[^[:space:];|&]+)*[[:space:]]+add([[:space:]]+[^[:space:];|&]+)*[[:space:]]+--upgrade([^[:alnum:]_]|$)'
readonly APT_UPGRADE_RE='(^|[^[:alnum:]_])apt(-get)?([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(upgrade|dist-upgrade|full-upgrade)([^[:alnum:]_]|$)'
readonly APT_INSTALL_ONLY_UPGRADE_RE='(^|[^[:alnum:]_])apt(-get)?([[:space:]]+[^[:space:];|&]+)*[[:space:]]+install([[:space:]]+[^[:space:];|&]+)*[[:space:]]+--only-upgrade([^[:alnum:]_]|$)'
readonly APT_ONLY_UPGRADE_RE='(^|[^[:alnum:]_])apt(-get)?[^;|&]*--only-upgrade([^[:alnum:]_]|$)'
readonly DNF_UPGRADE_RE='(^|[^[:alnum:]_])dnf([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(upgrade|update|system-upgrade|distro-sync)([^[:alnum:]_]|$)'
readonly YUM_UPGRADE_RE='(^|[^[:alnum:]_])yum([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(update|upgrade|distro-sync)([^[:alnum:]_]|$)'
readonly ZYPPER_UPGRADE_RE='(^|[^[:alnum:]_])zypper([[:space:]]+[^[:space:];|&]+)*[[:space:]]+(update|upgrade|dup|patch)([^[:alnum:]_]|$)'
readonly PACMAN_SHORT_UPGRADE_RE='(^|[^[:alnum:]_])pacman[^;|&]*-syu([^[:alnum:]_]|$)'
readonly PACMAN_SYSUPGRADE_RE='(^|[^[:alnum:]_])pacman[^;|&]*--sysupgrade([^[:alnum:]_]|$)'
readonly PACMAN_SEPARATED_SYNC_RE='(^|[^[:alnum:]_])pacman[^;|&]*[[:space:]]-s([^[:alnum:]_]|$)'
readonly PACMAN_SEPARATED_REFRESH_RE='(^|[^[:alnum:]_])pacman[^;|&]*[[:space:]]-y([^[:alnum:]_]|$)'
readonly PACMAN_SEPARATED_SYSUPGRADE_RE='(^|[^[:alnum:]_])pacman[^;|&]*[[:space:]]-u([^[:alnum:]_]|$)'

for path in "$@"; do
  if [[ ! -e "$path" ]]; then
    echo "ERROR: Dockerfile path does not exist: $path" >&2
    exit 1
  fi
done

if (( $# == 0 )); then
  mapfile -t DOCKERFILES < <(
    find "$ROOT_DIR" \
      \( -type d \( -path "$FIXTURE_DIR" -o -name .git -o -name node_modules -o -name bin -o -name obj \) -prune \) -o \
      \( -type f \( -name Dockerfile -o -name 'Dockerfile.*' -o -name '*.Dockerfile' \) -print \) \
      | LC_ALL=C sort
  )
else
  mapfile -t DOCKERFILES < <(
    for path in "$@"; do
      if [[ -d "$path" ]]; then
        find "$path" -type f \( -name Dockerfile -o -name 'Dockerfile.*' -o -name '*.Dockerfile' \) -print
      else
        printf '%s\n' "$path"
      fi
    done | LC_ALL=C sort -u
  )
fi

if (( ${#DOCKERFILES[@]} == 0 )); then
  echo "ERROR: no repository Dockerfiles found below $ROOT_DIR" >&2
  exit 1
fi

is_live_upgrade() {
  local line="$1"

  if [[ "$line" =~ $APK_UPGRADE_RE ]] ||
    [[ "$line" =~ $APK_ADD_UPGRADE_RE ]] ||
    [[ "$line" =~ $APT_UPGRADE_RE ]] ||
    [[ "$line" =~ $APT_INSTALL_ONLY_UPGRADE_RE ]] ||
    [[ "$line" =~ $APT_ONLY_UPGRADE_RE ]] ||
    [[ "$line" =~ $DNF_UPGRADE_RE ]] ||
    [[ "$line" =~ $YUM_UPGRADE_RE ]] ||
    [[ "$line" =~ $ZYPPER_UPGRADE_RE ]] ||
    [[ "$line" =~ $PACMAN_SHORT_UPGRADE_RE ]] ||
    [[ "$line" =~ $PACMAN_SYSUPGRADE_RE ]]; then
    return 0
  fi

  if [[ "$line" =~ $PACMAN_SEPARATED_SYNC_RE ]] &&
    [[ "$line" =~ $PACMAN_SEPARATED_REFRESH_RE ]] &&
    [[ "$line" =~ $PACMAN_SEPARATED_SYSUPGRADE_RE ]]; then
    return 0
  fi

  return 1
}

check_from_instruction() {
  local logical_line="$1"
  local display_path="$2"
  local line_number="$3"
  local trimmed="${logical_line#"${logical_line%%[![:space:]]*}"}"

  if [[ ! "$trimmed" =~ ^[Ff][Rr][Oo][Mm]([[:space:]]|$) ]]; then
    return
  fi

  local from_arguments="${trimmed:4}"
  local tokens=()
  local token
  local image_token=""
  read -r -a tokens <<< "$from_arguments"
  for token in "${tokens[@]}"; do
    if [[ "$token" == --* ]]; then
      continue
    fi
    image_token="$token"
    break
  done

  if [[ ! "$image_token" =~ $FROM_IMAGE_RE ]]; then
    echo "FAIL: $display_path:$line_number: FROM must use a lowercase 64-hex @sha256 digest in the image token (trailing comments do not count)" >&2
    failures=$((failures + 1))
  fi
}

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
  echo "CHECK: $display_path"

  line_number=0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))
    instruction_line="$line"
    instruction_start_line=$line_number

    while [[ "$instruction_line" =~ \\[[:space:]]*$ ]]; do
      trimmed_instruction="${instruction_line%"${instruction_line##*[![:space:]]}"}"
      instruction_line="${trimmed_instruction%\\}"
      if IFS= read -r continuation_line; then
        line_number=$((line_number + 1))
        instruction_line+=" $continuation_line"
      else
        break
      fi
    done

    check_from_instruction "$instruction_line" "$display_path" "$instruction_start_line"

    lower_instruction_line="${instruction_line,,}"
    if is_live_upgrade "$lower_instruction_line"; then
      echo "FAIL: $display_path:$instruction_start_line: live package upgrade is forbidden; install packages in the base image or pin individual packages" >&2
      failures=$((failures + 1))
    fi
  done < "$dockerfile"
done

if (( failures > 0 )); then
  echo "Dockerfile pin check: FAIL ($failures violation(s))" >&2
  exit 1
fi

echo "Dockerfile pin check: PASS (${#DOCKERFILES[@]} Dockerfile(s))"
