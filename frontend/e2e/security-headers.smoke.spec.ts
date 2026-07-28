import { expect, test } from '@playwright/test'

test('nginx serves a restrictive content security policy', async ({ request }) => {
  const response = await request.get('/')

  expect(response.ok()).toBe(true)

  const headers = response.headers()
  const csp = headers['content-security-policy']
  expect(csp).toContain("default-src 'self'")
  expect(csp).toContain("object-src 'none'")
  expect(csp).toContain("frame-ancestors 'none'")
  expect(csp).not.toContain("'unsafe-inline'")
  expect(csp).not.toContain("'unsafe-eval'")
  expect(headers['permissions-policy']).toBe(
    'camera=(), microphone=(), geolocation=()',
  )
})
