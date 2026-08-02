#!/usr/bin/env bash
# Samples API/web/PostgreSQL container CPU and memory once per second through
# `docker stats --no-stream` and appends newline-delimited JSON to the output
# file. Stops cleanly on SIGINT/SIGTERM and never prints environment values.
set -euo pipefail

OUTPUT_FILE="${1:-performance/compose-stats.ndjson}"
INTERVAL_SEC="${STATS_INTERVAL_SEC:-1}"
CONTAINERS=("vshelpdesk-perf-db-1" "vshelpdesk-perf-api-1" "vshelpdesk-perf-web-1")

running=true
trap 'running=false' INT TERM

strip_percent() {
  local raw="$1"
  printf '%s' "${raw%\%}"
}

echo "Writing container stats to ${OUTPUT_FILE}; press Ctrl+C to stop."
while [ "${running}" = true ]; do
  timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  while IFS='|' read -r name cpu memory_usage memory_percent; do
    [ -n "${name}" ] || continue
    printf '{"ts":"%s","container":"%s","cpuPercent":%s,"memoryUsage":"%s","memoryPercent":%s}\n' \
      "${timestamp}" \
      "${name}" \
      "$(strip_percent "${cpu}")" \
      "${memory_usage}" \
      "$(strip_percent "${memory_percent}")" >> "${OUTPUT_FILE}"
  done < <(docker stats --no-stream \
    --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}' \
    "${CONTAINERS[@]}" 2>/dev/null || true)

  sleep "${INTERVAL_SEC}" &
  wait $! 2>/dev/null || true
done

echo "Stopped sampling; ${OUTPUT_FILE} retained."
