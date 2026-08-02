import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { gzipSync } from 'node:zlib'
import {
  budgets,
  evaluateBudgets,
  measureGzipAssets,
} from './check-bundle-budget.mjs'

const KIB = 1024

async function makeDist() {
  const root = await mkdtemp(join(tmpdir(), 'bundle-budget-'))
  const assets = join(root, 'dist', 'assets')
  await mkdir(assets, { recursive: true })
  return {
    dist: join(root, 'dist'),
    assets,
    async cleanup() {
      await rm(root, { recursive: true, force: true })
    },
  }
}

test('sums hashed .js chunks by raw and gzip bytes', async () => {
  const fixture = await makeDist()
  try {
    const first = Buffer.from('const a = "first chunk";\n'.repeat(50))
    const second = Buffer.from('const b = "second chunk";\n'.repeat(40))
    await writeFile(join(fixture.assets, 'index-AAAA1111.js'), first)
    await writeFile(join(fixture.assets, 'vendor-BBBB2222.js'), second)
    await writeFile(join(fixture.assets, 'index-CCCC3333.css'), 'body{}\n')

    const measured = await measureGzipAssets(fixture.dist)

    assert.equal(measured.js.files, 2)
    assert.equal(measured.js.raw, first.byteLength + second.byteLength)
    assert.equal(
      measured.js.gzip,
      gzipSync(first, { level: 9 }).byteLength +
        gzipSync(second, { level: 9 }).byteLength,
    )
  } finally {
    await fixture.cleanup()
  }
})

test('sums hashed .css assets by raw and gzip bytes', async () => {
  const fixture = await makeDist()
  try {
    const first = Buffer.from('.a { color: red; }\n'.repeat(60))
    const second = Buffer.from('.b { color: blue; }\n'.repeat(30))
    await writeFile(join(fixture.assets, 'index-AAAA1111.css'), first)
    await writeFile(join(fixture.assets, 'theme-BBBB2222.css'), second)

    const measured = await measureGzipAssets(fixture.dist)

    assert.equal(measured.css.files, 2)
    assert.equal(measured.css.raw, first.byteLength + second.byteLength)
    assert.equal(
      measured.css.gzip,
      gzipSync(first, { level: 9 }).byteLength +
        gzipSync(second, { level: 9 }).byteLength,
    )
  } finally {
    await fixture.cleanup()
  }
})

test('ignores source maps, un-hashed files, and unrelated assets', async () => {
  const fixture = await makeDist()
  try {
    const bundleJs = Buffer.from('const real = "counted";\n'.repeat(30))
    const bundleCss = Buffer.from('.real { color: green; }\n'.repeat(20))
    await writeFile(join(fixture.assets, 'index-AAAA1111.js'), bundleJs)
    await writeFile(join(fixture.assets, 'index-AAAA1111.js.map'), '{}')
    await writeFile(join(fixture.assets, 'index-AAAA1111.css'), bundleCss)
    await writeFile(join(fixture.assets, 'index-AAAA1111.css.map'), '{}')
    await writeFile(join(fixture.assets, 'manual.js'), 'ignored')
    await writeFile(join(fixture.assets, 'legacy.css'), 'ignored')
    await writeFile(join(fixture.assets, 'logo-AAAA1111.svg'), '<svg/>')
    await writeFile(
      join(fixture.assets, 'manrope-latin-wght-normal-usUDDRr7.woff2'),
      'font-bytes',
    )
    await writeFile(join(fixture.assets, 'notes.txt'), 'ignored')

    const measured = await measureGzipAssets(fixture.dist)

    assert.equal(measured.js.files, 1)
    assert.equal(measured.js.raw, bundleJs.byteLength)
    assert.equal(measured.css.files, 1)
    assert.equal(measured.css.raw, bundleCss.byteLength)
  } finally {
    await fixture.cleanup()
  }
})

test('passes at exactly 120 KiB JS and 15 KiB CSS gzip', () => {
  const evaluation = evaluateBudgets({
    js: { raw: budgets.js.allowed * 4, gzip: budgets.js.allowed, files: 1 },
    css: { raw: budgets.css.allowed * 4, gzip: budgets.css.allowed, files: 1 },
  })

  assert.equal(evaluation.ok, true)
  assert.deepEqual(evaluation.failures, [])
})

test('fails one byte above either limit and reports measured and allowed bytes', () => {
  const overJs = evaluateBudgets({
    js: { raw: budgets.js.allowed * 4, gzip: budgets.js.allowed + 1, files: 1 },
    css: { raw: 1000, gzip: 1000, files: 1 },
  })
  assert.equal(overJs.ok, false)
  assert.equal(overJs.failures.length, 1)
  assert.match(overJs.failures[0], /JavaScript/)
  assert.match(overJs.failures[0], new RegExp(String(budgets.js.allowed + 1)))
  assert.match(overJs.failures[0], new RegExp(String(budgets.js.allowed)))

  const overCss = evaluateBudgets({
    js: { raw: 1000, gzip: 1000, files: 1 },
    css: {
      raw: budgets.css.allowed * 4,
      gzip: budgets.css.allowed + 1,
      files: 1,
    },
  })
  assert.equal(overCss.ok, false)
  assert.equal(overCss.failures.length, 1)
  assert.match(overCss.failures[0], /CSS/)
  assert.match(overCss.failures[0], new RegExp(String(budgets.css.allowed + 1)))
  assert.match(overCss.failures[0], new RegExp(String(budgets.css.allowed)))

  const bothOver = evaluateBudgets({
    js: { raw: 0, gzip: budgets.js.allowed + 1, files: 1 },
    css: { raw: 0, gzip: budgets.css.allowed + 1, files: 1 },
  })
  assert.equal(bothOver.ok, false)
  assert.equal(bothOver.failures.length, 2)
})

test('missing JS or CSS assets fail closed', () => {
  const noJs = evaluateBudgets({
    js: { raw: 0, gzip: 0, files: 0 },
    css: { raw: 1000, gzip: 1000, files: 1 },
  })
  assert.equal(noJs.ok, false)
  assert.equal(noJs.failures.length, 1)
  assert.match(noJs.failures[0], /JavaScript/)

  const noCss = evaluateBudgets({
    js: { raw: 1000, gzip: 1000, files: 1 },
    css: { raw: 0, gzip: 0, files: 0 },
  })
  assert.equal(noCss.ok, false)
  assert.equal(noCss.failures.length, 1)
  assert.match(noCss.failures[0], /CSS/)
})

test('a missing assets directory fails closed instead of passing', async () => {
  const fixture = await makeDist()
  try {
    await rm(fixture.assets, { recursive: true, force: true })
    await assert.rejects(() => measureGzipAssets(fixture.dist), /assets/)
  } finally {
    await fixture.cleanup()
  }
})

test('120 KiB and 15 KiB budgets are the documented limits', () => {
  assert.equal(budgets.js.allowed, 120 * KIB)
  assert.equal(budgets.css.allowed, 15 * KIB)
})
