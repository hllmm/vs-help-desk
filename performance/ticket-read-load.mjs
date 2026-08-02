#!/usr/bin/env node
// Dependency-free ticket-read load runner (Node.js 22 built-ins only).
// Exercises the bounded read path through cookie-authenticated HTTP:
//   50% GET /api/tickets?pageSize=50
//   20% GET /api/tickets?status=New&pageSize=50
//   15% GET /api/tickets?search=VS-PERF&pageSize=50
//   15% GET /api/tickets/{id} for an id captured from a list response
//
// Never logs cookies, credentials, response bodies, or cursors.

const REQUEST_TIMEOUT_MS = 10_000
const LOGIN_PACE_MS = 6_500
const LOGIN_429_RETRY_MS = 65_000
const LOGIN_MAX_ATTEMPTS = 6
const MAX_REMEMBERED_TICKET_IDS = 200

function readIntEnv(name, fallback) {
  const raw = process.env[name]
  if (raw === undefined || raw.trim() === '') {
    return fallback
  }
  const value = Number.parseInt(raw, 10)
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`Environment variable ${name} must be a non-negative integer (got "${raw}").`)
  }
  return value
}

function readFloatEnv(name, fallback) {
  const raw = process.env[name]
  if (raw === undefined || raw.trim() === '') {
    return fallback
  }
  const value = Number.parseFloat(raw)
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`Environment variable ${name} must be a non-negative number (got "${raw}").`)
  }
  return value
}

function readConfig() {
  const baseUrl = (process.env.PERF_BASE_URL ?? 'http://127.0.0.1:8080').replace(/\/+$/, '')
  const username = process.env.PERF_USERNAME?.trim() ?? ''
  const password = process.env.PERF_PASSWORD ?? ''
  if (!username || !password) {
    throw new Error('PERF_USERNAME and PERF_PASSWORD are required.')
  }
  return {
    baseUrl,
    username,
    password,
    vus: readIntEnv('PERF_VUS', 20) || 1,
    durationSec: readIntEnv('PERF_DURATION_SEC', 60) || 1,
    warmupSec: readIntEnv('PERF_WARMUP_SEC', 15),
    p95Ms: readFloatEnv('PERF_P95_MS', 2_000),
    p99Ms: readFloatEnv('PERF_P99_MS', 3_000),
    maxErrorRate: readFloatEnv('PERF_MAX_ERROR_RATE', 0.01),
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function cookieHeaderFrom(setCookies) {
  const pairs = new Map()
  for (const header of setCookies) {
    const pair = header.split(';', 1)[0]
    const separator = pair.indexOf('=')
    if (separator > 0) {
      pairs.set(pair.slice(0, separator).trim(), pair.slice(separator + 1).trim())
    }
  }
  return [...pairs.entries()].map(([name, value]) => `${name}=${value}`).join('; ')
}

async function login(config, vuIndex) {
  for (let attempt = 1; attempt <= LOGIN_MAX_ATTEMPTS; attempt += 1) {
    let response
    try {
      response = await fetch(`${config.baseUrl}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ username: config.username, password: config.password }),
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      })
    } catch (error) {
      throw new Error(`VU ${vuIndex}: login transport failure: ${error.name}`)
    }

    if (response.status === 200) {
      const cookie = cookieHeaderFrom(response.headers.getSetCookie())
      if (!cookie.includes('vshd.auth=')) {
        throw new Error(`VU ${vuIndex}: login response missed the auth cookie.`)
      }
      await response.arrayBuffer().catch(() => {})
      return { cookie }
    }

    const retryAfterSeconds = Number.parseInt(response.headers.get('retry-after') ?? '', 10)
    await response.arrayBuffer().catch(() => {})
    if (response.status === 429 && attempt < LOGIN_MAX_ATTEMPTS) {
      const waitMs = Number.isFinite(retryAfterSeconds)
        ? retryAfterSeconds * 1_000
        : LOGIN_429_RETRY_MS
      console.log(`VU ${vuIndex}: login hit the rate limit; retrying in ${Math.round(waitMs / 1000)}s.`)
      await sleep(waitMs)
      continue
    }
    throw new Error(`VU ${vuIndex}: login failed with HTTP ${response.status}.`)
  }
  throw new Error(`VU ${vuIndex}: login exhausted ${LOGIN_MAX_ATTEMPTS} attempts.`)
}

function chooseShape(random, hasTicketIds) {
  if (random < 0.5) {
    return 'list-all'
  }
  if (random < 0.7) {
    return 'list-status-new'
  }
  if (random < 0.85) {
    return 'list-search'
  }
  return hasTicketIds ? 'ticket-detail' : 'list-all'
}

function requestPath(shape, ticketId) {
  switch (shape) {
    case 'list-all':
      return '/api/tickets?pageSize=50'
    case 'list-status-new':
      return '/api/tickets?status=New&pageSize=50'
    case 'list-search':
      return '/api/tickets?search=VS-PERF&pageSize=50'
    case 'ticket-detail':
      return `/api/tickets/${encodeURIComponent(ticketId)}`
    default:
      throw new Error(`Unknown request shape ${shape}`)
  }
}

async function executeSample(config, session, shape, ticketId) {
  const started = performance.now()
  try {
    const response = await fetch(`${config.baseUrl}${requestPath(shape, ticketId)}`, {
      method: 'GET',
      headers: { Accept: 'application/json', Cookie: session.cookie },
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    })
    const previewIds = []
    if (response.status === 200 && shape !== 'ticket-detail') {
      const page = await response.json().catch(() => null)
      if (page && Array.isArray(page.items)) {
        for (const item of page.items) {
          if (item && typeof item.id === 'string') {
            previewIds.push(item.id)
          }
        }
      }
    } else {
      await response.arrayBuffer().catch(() => {})
    }
    return {
      shape,
      durationMs: performance.now() - started,
      status: response.status,
      transportError: false,
      previewIds,
    }
  } catch {
    return {
      shape,
      durationMs: performance.now() - started,
      status: 0,
      transportError: true,
      previewIds: [],
    }
  }
}

function nearestRank(sortedDurations, percentile) {
  if (sortedDurations.length === 0) {
    return 0
  }
  const rank = Math.ceil((percentile / 100) * sortedDurations.length)
  return sortedDurations[Math.min(Math.max(rank, 1), sortedDurations.length) - 1]
}

function summarizeDurations(durations) {
  const sorted = [...durations].sort((a, b) => a - b)
  return {
    samples: durations.length,
    p50: nearestRank(sorted, 50),
    p95: nearestRank(sorted, 95),
    p99: nearestRank(sorted, 99),
    max: sorted.length === 0 ? 0 : sorted[sorted.length - 1],
  }
}

function round(value) {
  return Math.round(value * 100) / 100
}

function buildSummary(config, samples, startIso) {
  const measured = samples.filter((sample) => !sample.warmup)
  const errors = measured.filter((sample) => sample.transportError || sample.status !== 200)
  const errorRate = measured.length === 0 ? 1 : errors.length / measured.length

  const endpoints = {}
  for (const shape of ['list-all', 'list-status-new', 'list-search', 'ticket-detail']) {
    const shapeSamples = measured.filter((sample) => sample.shape === shape)
    const shapeErrors = shapeSamples.filter(
      (sample) => sample.transportError || sample.status !== 200,
    )
    const statusCounts = {}
    for (const sample of shapeSamples) {
      const key = sample.transportError ? 'transport' : String(sample.status)
      statusCounts[key] = (statusCounts[key] ?? 0) + 1
    }
    endpoints[shape] = {
      ...summarizeDurations(shapeSamples.map((sample) => sample.durationMs).map(round)),
      errors: shapeErrors.length,
      statusCounts,
    }
  }

  const overall = summarizeDurations(measured.map((sample) => round(sample.durationMs)))
  const slo = {
    p95Ms: { threshold: config.p95Ms, measured: overall.p95, pass: overall.p95 < config.p95Ms },
    p99Ms: { threshold: config.p99Ms, measured: overall.p99, pass: overall.p99 < config.p99Ms },
    errorRate: {
      threshold: config.maxErrorRate,
      measured: round(errorRate * 10_000) / 10_000,
      pass: errorRate < config.maxErrorRate,
    },
    samplesPresent: measured.length > 0,
  }
  const pass = slo.p95Ms.pass && slo.p99Ms.pass && slo.errorRate.pass && slo.samplesPresent

  return {
    measured,
    summary: {
      evidence: 'ticket-read-load',
      startedAtUtc: startIso,
      vus: config.vus,
      durationSec: config.durationSec,
      warmupSec: config.warmupSec,
      totals: {
        samples: measured.length,
        errors: errors.length,
        errorRate: round(errorRate * 10_000) / 10_000,
        p50: overall.p50,
        p95: overall.p95,
        p99: overall.p99,
        max: overall.max,
      },
      endpoints,
      slo,
      pass,
    },
  }
}

function printHumanTable(summary) {
  const rows = [
    ['endpoint', 'samples', 'errors', 'p50 ms', 'p95 ms', 'p99 ms', 'max ms'],
    ...Object.entries(summary.endpoints).map(([name, stats]) => [
      name,
      String(stats.samples),
      String(stats.errors),
      stats.p50.toFixed(0),
      stats.p95.toFixed(0),
      stats.p99.toFixed(0),
      stats.max.toFixed(0),
    ]),
    [
      'ALL',
      String(summary.totals.samples),
      String(summary.totals.errors),
      summary.totals.p50.toFixed(0),
      summary.totals.p95.toFixed(0),
      summary.totals.p99.toFixed(0),
      summary.totals.max.toFixed(0),
    ],
  ]
  const widths = rows[0].map((_, column) =>
    Math.max(...rows.map((row) => row[column].length)),
  )
  for (const row of rows) {
    console.log(row.map((cell, column) => cell.padEnd(widths[column])).join('  '))
  }
  console.log(
    `SLO: p95<${summary.slo.p95Ms.threshold}ms ${summary.slo.p95Ms.pass ? 'PASS' : 'FAIL'}; ` +
      `p99<${summary.slo.p99Ms.threshold}ms ${summary.slo.p99Ms.pass ? 'PASS' : 'FAIL'}; ` +
      `error rate<${summary.slo.errorRate.threshold} ${summary.slo.errorRate.pass ? 'PASS' : 'FAIL'}`,
  )
}

async function waitForHealth(config) {
  for (let attempt = 1; attempt <= 5; attempt += 1) {
    try {
      const response = await fetch(`${config.baseUrl}/health`, {
        headers: { Accept: 'application/json' },
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      })
      await response.arrayBuffer().catch(() => {})
      if (response.status === 200) {
        return
      }
    } catch {
      // retry below
    }
    if (attempt < 5) {
      console.log('Waiting for the API health endpoint...')
      await sleep(2_000)
    }
  }
  throw new Error(`API health endpoint did not answer with 200 at ${config.baseUrl}.`)
}

async function runVirtualUser(config, vuIndex, session, samples, warmupEndMs, endMs) {
  const ticketIds = []
  // Small deterministic stagger spreads the first requests without extra sleeps later.
  await sleep((vuIndex * 137) % 500)
  while (performance.now() < endMs) {
    const shape = chooseShape(Math.random(), ticketIds.length > 0)
    const ticketId =
      shape === 'ticket-detail'
        ? ticketIds[Math.floor(Math.random() * ticketIds.length)]
        : undefined
    const sample = await executeSample(config, session, shape, ticketId)
    sample.warmup = performance.now() < warmupEndMs
    samples.push(sample)
    for (const id of sample.previewIds) {
      ticketIds.push(id)
    }
    if (ticketIds.length > MAX_REMEMBERED_TICKET_IDS) {
      ticketIds.splice(0, ticketIds.length - MAX_REMEMBERED_TICKET_IDS)
    }
  }
}

async function main() {
  let config
  try {
    config = readConfig()
  } catch (error) {
    console.error(`CONFIGURATION ERROR: ${error.message}`)
    process.exit(2)
  }

  try {
    await waitForHealth(config)
  } catch (error) {
    console.error(`PREFLIGHT FAILURE: ${error.message}`)
    process.exit(2)
  }

  console.log(
    `Logging in ${config.vus} virtual users one by one, paced every ${LOGIN_PACE_MS / 1_000}s ` +
      'to respect the production login rate limit...',
  )
  const sessions = []
  for (let vuIndex = 0; vuIndex < config.vus; vuIndex += 1) {
    try {
      sessions.push(await login(config, vuIndex + 1))
    } catch (error) {
      console.error(`LOGIN FAILURE: ${error.message}`)
      process.exit(3)
    }
    if (vuIndex + 1 < config.vus) {
      await sleep(LOGIN_PACE_MS)
    }
  }

  const samples = []
  const startMs = performance.now()
  const warmupEndMs = startMs + config.warmupSec * 1_000
  const endMs = warmupEndMs + config.durationSec * 1_000
  const startIso = new Date().toISOString()

  console.log(
    `Running ${config.vus} virtual users: ${config.warmupSec}s warm-up + ${config.durationSec}s measured.`,
  )
  await Promise.all(
    sessions.map((session, vuIndex) =>
      runVirtualUser(config, vuIndex + 1, session, samples, warmupEndMs, endMs),
    ),
  )

  const { summary } = buildSummary(config, samples, startIso)

  const warmups = samples.length - summary.totals.samples
  console.log(`Completed. Measured ${summary.totals.samples} samples (${warmups} warm-up samples discarded).`)
  printHumanTable(summary)
  console.log(`PERF_RESULT ${JSON.stringify(summary)}`)

  if (!summary.slo.samplesPresent) {
    console.error('No measured samples recorded; refusing to pass.')
    process.exit(4)
  }
  process.exit(summary.pass ? 0 : 1)
}

await main()
