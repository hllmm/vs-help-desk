// Dependency-free production bundle budget gate (Node.js built-ins only).
// Sums the gzip size of hashed Vite assets under dist/assets and enforces:
// JavaScript <= 120 KiB gzip, CSS <= 15 KiB gzip.

import { readdir, readFile } from 'node:fs/promises'
import { extname, join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { gzipSync } from 'node:zlib'

const KIB = 1024

export const budgets = Object.freeze({
  js: Object.freeze({ label: 'JavaScript', allowed: 120 * KIB }),
  css: Object.freeze({ label: 'CSS', allowed: 15 * KIB }),
})

const HASHED_ASSET_PATTERN = /-[A-Za-z0-9_-]{8,}\.[a-z0-9]+$/
const JS_EXTENSIONS = new Set(['.js', '.mjs'])

function assetKind(fileName) {
  if (fileName.endsWith('.map') || !HASHED_ASSET_PATTERN.test(fileName)) {
    return null
  }
  const extension = extname(fileName).toLowerCase()
  if (JS_EXTENSIONS.has(extension)) {
    return 'js'
  }
  if (extension === '.css') {
    return 'css'
  }
  return null
}

/**
 * Measures raw and gzip byte totals for hashed JS/CSS bundle assets.
 * Throws when the assets directory is missing so the gate fails closed.
 */
export async function measureGzipAssets(distDir) {
  const assetsDir = join(distDir, 'assets')
  let entries
  try {
    entries = await readdir(assetsDir, { withFileTypes: true })
  } catch (error) {
    throw new Error(
      `Bundle assets directory is missing or unreadable: ${assetsDir} (${error.message})`,
    )
  }

  const measured = {
    js: { raw: 0, gzip: 0, files: 0 },
    css: { raw: 0, gzip: 0, files: 0 },
  }
  for (const entry of entries) {
    if (!entry.isFile()) {
      continue
    }
    const kind = assetKind(entry.name)
    if (kind === null) {
      continue
    }
    const content = await readFile(join(assetsDir, entry.name))
    measured[kind].raw += content.byteLength
    measured[kind].gzip += gzipSync(content, { level: 9 }).byteLength
    measured[kind].files += 1
  }
  return measured
}

/**
 * Pure budget evaluation. Fails closed on missing assets and reports the
 * measured vs allowed gzip bytes for every breach.
 */
export function evaluateBudgets(measured) {
  const failures = []
  for (const kind of Object.keys(budgets)) {
    const { label, allowed } = budgets[kind]
    const actual = measured[kind]
    if (!actual || actual.files === 0) {
      failures.push(
        `${label}: no bundle assets found; refusing to pass without measured gzip bytes (allowed ${allowed}).`,
      )
      continue
    }
    if (actual.gzip > allowed) {
      failures.push(
        `${label}: measured gzip ${actual.gzip} bytes exceeds the allowed ${allowed} bytes.`,
      )
    }
  }
  return { ok: failures.length === 0, failures }
}

function formatBytes(bytes) {
  return `${bytes} B (${(bytes / KIB).toFixed(2)} KiB)`
}

function printReport(measured) {
  for (const kind of Object.keys(budgets)) {
    const { label, allowed } = budgets[kind]
    const actual = measured[kind]
    console.log(
      `${label}: ${actual.files} file(s), ` +
        `raw ${formatBytes(actual.raw)}, ` +
        `gzip ${formatBytes(actual.gzip)} ` +
        `(allowed gzip ${formatBytes(allowed)})`,
    )
  }
}

export async function checkBundleBudget(distDir) {
  const measured = await measureGzipAssets(distDir)
  printReport(measured)
  const evaluation = evaluateBudgets(measured)
  if (!evaluation.ok) {
    for (const failure of evaluation.failures) {
      console.error(`BUDGET FAILURE: ${failure}`)
    }
    return 1
  }
  console.log('Bundle budgets passed.')
  return 0
}

const invokedFile = process.argv[1] ? resolve(process.argv[1]) : ''
if (invokedFile && import.meta.url === pathToFileURL(invokedFile).href) {
  const distDir = process.argv[2] ?? 'dist'
  checkBundleBudget(distDir)
    .then((exitCode) => {
      process.exit(exitCode)
    })
    .catch((error) => {
      console.error(`BUDGET FAILURE: ${error.message}`)
      process.exit(1)
    })
}
