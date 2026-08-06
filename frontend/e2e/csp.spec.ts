import { expect, test } from '@playwright/test'

test('csp and hardened headers present on document', async ({ request, page }) => {
  const resp = await request.get('/')
  expect(resp.ok()).toBeTruthy()

  const headers = resp.headers()
  const csp = headers['content-security-policy'] ?? ''
  expect(csp).toContain("default-src 'self'")
  expect(csp).toContain("script-src 'self'")
  expect(csp).toContain("style-src 'self'")
  // allow unsafe-inline for Vite/React inline style attributes
  expect(csp).toContain("'unsafe-inline'")
  expect(csp).toContain("object-src 'none'")
  expect(csp).toContain("base-uri 'none'")
  expect(csp).toContain("frame-ancestors 'none'")
  expect(csp).toContain("form-action 'self'")
  expect(csp).toContain("img-src 'self' data:")
  expect(csp).toContain("connect-src 'self'")
  expect(csp).toContain("font-src 'self'")

  expect(headers['permissions-policy']).toContain('camera=()')
  expect(headers['permissions-policy']).toContain('microphone=()')
  expect(headers['permissions-policy']).toContain('geolocation=()')

  expect(headers['x-content-type-options']?.toLowerCase()).toBe('nosniff')
  expect(headers['x-frame-options']).toBe('DENY')
  expect(headers['referrer-policy']).toBe('strict-origin-when-cross-origin')

  // Also ensure page renders without CSP console violations
  const consoleErrors: string[] = []
  page.on('console', (msg) => {
    if (msg.type() === 'error' && /Content Security Policy/i.test(msg.text())) {
      consoleErrors.push(msg.text())
    }
  })
  await page.goto('/')
  await expect(page.locator('#root')).toBeAttached()
  expect(consoleErrors, consoleErrors.join('\n')).toEqual([])
})
