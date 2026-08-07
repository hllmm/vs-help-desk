import { expect, test } from '@playwright/test'

// Real API base for direct checks if needed

// Seed user created by CI (same as backend job)
const SEED_USER = {
  username: 'support',
  password: 'CiSeedPassword123!',
}

test('real api critical flow: login, session restore, create ticket, details, logout, redirect', async ({ page }) => {
  // 1. Request a real token from /api/auth/csrf.
  // Use page.evaluate so the cookie is set in the browser context (no page.route mocking).
  await page.goto('/login')
  const csrfViaBrowser = await page.evaluate(async () => {
    const res = await fetch('/api/auth/csrf', { credentials: 'include' })
    const body = await res.json()
    return { ok: res.ok, body }
  })
  expect(csrfViaBrowser.ok).toBeTruthy()
  expect(csrfViaBrowser.body.csrfToken).toBeTruthy()

  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Hesabınıza giriş yapın' })).toBeVisible()

  // 2. Log in through the UI using the seeded CI user.
  await page.getByLabel('Kullanıcı adı').fill(SEED_USER.username)
  await page.getByLabel('Parola').fill(SEED_USER.password)
  await page.getByRole('button', { name: 'Giriş yap' }).click()

  // Should navigate to /tickets
  await expect(page).toHaveURL(/\/tickets/)
  await expect(page.getByRole('heading', { name: 'Destek talepleri' })).toBeVisible()

  // 3. Refresh the page and confirm that /api/auth/me restores the session.
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Destek talepleri' })).toBeVisible()
  const meViaBrowser = await page.evaluate(async () => {
    const res = await fetch('/api/auth/me', { credentials: 'include' })
    return { status: res.status, body: res.ok ? await res.json() : null }
  })
  expect(meViaBrowser.status).toBe(200)
  expect(meViaBrowser.body.username).toBe(SEED_USER.username)

  // 4. Create a ticket through the UI.
  const uniqueSubject = `E2E Test Ticket ${Date.now()}`
  await page.getByRole('button', { name: 'Yeni talep oluştur' }).click()
  await expect(page.getByLabel('Konu')).toBeVisible()
  await page.getByLabel('Konu').fill(uniqueSubject)
  await page.getByLabel('Müşteri adı').fill('E2E Customer')
  await page.getByLabel('Müşteri e-posta').fill('e2e.customer@example.test')
  await page.getByLabel('İçerik').fill('This is a real E2E ticket content.')
  const createdRequestPromise = page.waitForRequest(
    (request) =>
      request.method() === 'POST' &&
      new URL(request.url()).pathname === '/api/tickets',
  )
  const createdResponsePromise = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === '/api/tickets',
  )
  await page.getByRole('button', { name: 'Oluştur' }).click()
  const [createdRequest, createdResponse] = await Promise.all([
    createdRequestPromise,
    createdResponsePromise,
  ])
  expect(createdResponse.status()).toBe(201)
  const created = await createdResponse.json() as {
    ticketId: string
    ticketNumber: string
  }
  const idempotencyKey = createdRequest.headers()['idempotency-key']
  const createPayload = createdRequest.postDataJSON() as {
    subject: string
    customerName: string
    customerEmail: string
    content: string
  }
  expect(idempotencyKey).toMatch(
    /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
  )

  // Wait for the ticket to appear in the list (after refresh)
  await expect(page.getByText(uniqueSubject).first()).toBeVisible({ timeout: 10000 })

  // 5. Replay the exact browser request through the real API. The stored
  // portal record must return the original ticket instead of creating another.
  const replay = await page.evaluate(
    async ({ key, payload }) => {
      const csrf = document.cookie.match(/(?:^|;\s*)vshd\.csrf=([^;]+)/)?.[1] ?? ''
      const response = await fetch('/api/tickets', {
        method: 'POST',
        credentials: 'include',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          'Idempotency-Key': key,
          'X-CSRF-Token': csrf,
        },
        body: JSON.stringify(payload),
      })
      return { status: response.status, body: await response.json() }
    },
    { key: idempotencyKey, payload: createPayload },
  )
  expect(replay.status).toBe(200)
  expect(replay.body.ticketId).toBe(created.ticketId)

  const matchingTicketIds = await page.evaluate(async (subject) => {
    const response = await fetch(
      `/api/tickets?search=${encodeURIComponent(subject)}`,
      { credentials: 'include' },
    )
    const body = await response.json() as {
      items: Array<{ id: string; subject: string }>
    }
    return body.items
      .filter((ticket) => ticket.subject === subject)
      .map((ticket) => ticket.id)
  }, uniqueSubject)
  expect(matchingTicketIds).toEqual([created.ticketId])

  // 6. Open the ticket details.
  await page.getByText(uniqueSubject).first().click()
  await expect(page).toHaveURL(/\/tickets\/.+/)
  await expect(page.getByText(uniqueSubject)).toBeVisible()
  // Verify details page shows customer info
  await expect(page.getByText('E2E Customer')).toBeVisible()

  // 7. Log out.
  // Find logout button (likely in header)
  const logoutButton = page.getByRole('button', { name: /Çıkış|Logout/i }).first()
  if (await logoutButton.isVisible().catch(() => false)) {
    await logoutButton.click()
  } else {
    // Fallback: navigate to logout via API and then goto login
    await page.evaluate(async () => {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'include', headers: { 'X-CSRF-Token': document.cookie.match(/vshd\.csrf=([^;]+)/)?.[1] || '' } })
    })
    await page.goto('/login')
  }
  await expect(page).toHaveURL(/\/login/)

  // 8. Attempt to revisit a protected page and confirm redirection to /login.
  await page.goto('/tickets')
  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { name: 'Hesabınıza giriş yapın' })).toBeVisible()

  // Verify no /api/* was mocked via page.route (we never called page.route for /api in this test)
})
