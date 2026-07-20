import {
  expect,
  test,
  type Locator,
  type Page,
  type TestInfo,
} from '@playwright/test'

const ticketFixtures = [
  {
    id: '22222222-2222-2222-2222-222222222222',
    ticketNumber: 'VS-000042',
    subject:
      'Uzak ofis yazıcısında tekrar eden ve ayrıntılı inceleme gerektiren bağlantı sorunu',
    customerName: 'İrem Uzunsoyad',
    customerEmail: 'irem.uzun.birim.adi@example.test',
    status: 'Escalated',
    lastActivityAt: '2026-07-20T15:30:00Z',
    assignedUserId: null,
  },
]

const LOGIN_API = 'http://127.0.0.1:4173/api/auth/login'
const TICKETS_API = 'http://127.0.0.1:4173/api/tickets'

const EXPIRY_NOTICE =
  'Oturumunuz sona erdi. Devam etmek için yeniden giriş yapın.'

function attachTelemetry(page: Page) {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  const failedRequests: string[] = []
  const requestUrls: string[] = []

  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => pageErrors.push(error.message))
  page.on('requestfailed', (request) => {
    const failure = request.failure()?.errorText ?? ''
    // StrictMode remount / navigation aborts are expected; real failures are not.
    if (
      failure.includes('ERR_ABORTED') ||
      failure.includes('NS_BINDING_ABORTED')
    ) {
      return
    }
    failedRequests.push(
      `${request.method()} ${request.url()} ${failure}`,
    )
  })
  page.on('request', (request) => {
    requestUrls.push(request.url())
  })

  return {
    consoleErrors,
    pageErrors,
    failedRequests,
    requestUrls,
    assertClean(options?: { allowUnauthorizedConsole?: boolean }) {
      const filteredConsole = options?.allowUnauthorizedConsole
        ? consoleErrors.filter(
            (text) =>
              !/Failed to load resource:.*401/.test(text) &&
              !/401 \(Unauthorized\)/.test(text),
          )
        : consoleErrors
      expect(filteredConsole, filteredConsole.join('\n')).toEqual([])
      expect(pageErrors, pageErrors.join('\n')).toEqual([])
      expect(failedRequests, failedRequests.join('\n')).toEqual([])
    },
  }
}

async function mockLoginSuccess(page: Page) {
  await page.route(LOGIN_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'browser-test-token',
        userId: '11111111-1111-1111-1111-111111111111',
        fullName: 'Ada Destek',
        username: 'ada.destek',
      }),
    })
  })
}

async function mockTicketsSuccess(page: Page) {
  await page.route(TICKETS_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ticketFixtures),
    })
  })
}

async function loginThroughUi(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Kullanıcı adı').fill('ada.destek')
  await page.getByLabel('Parola').fill('secret-password')
  await page.getByRole('button', { name: 'Giriş yap' }).click()
  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()
}

/** Prefer the currently displayed list surface (table on desktop, cards below 47.99rem). */
function visibleResultsRoot(page: Page): Locator {
  return page.locator(
    '.ticket-table-view:visible, .ticket-card-list:visible',
  )
}

async function assertNoDocumentOverflow(page: Page) {
  const dimensions = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
  }))
  expect(dimensions.scroll).toBeLessThanOrEqual(dimensions.client)
}

async function assertFieldInsideViewport(page: Page, text: string) {
  const locator = visibleResultsRoot(page).getByText(text).first()
  await expect(locator).toBeVisible()
  const box = await locator.boundingBox()
  expect(box).not.toBeNull()
  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  expect(box!.x).toBeGreaterThanOrEqual(-1)
  expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width + 1)
}

async function assertFocusVisibleOutline(page: Page) {
  const outline = await page.evaluate(() => {
    const el = document.activeElement
    if (!(el instanceof HTMLElement)) {
      return null
    }
    const style = getComputedStyle(el)
    return {
      outlineWidth: style.outlineWidth,
      outlineStyle: style.outlineStyle,
    }
  })
  expect(outline).not.toBeNull()
  expect(outline!.outlineStyle).not.toBe('none')
  expect(parseFloat(outline!.outlineWidth)).toBeGreaterThan(0)
}

async function tabUntil(
  page: Page,
  isMatch: () => Promise<boolean>,
  maxTabs = 24,
): Promise<void> {
  for (let i = 0; i < maxTabs; i += 1) {
    if (await isMatch()) {
      return
    }
    await page.keyboard.press('Tab')
  }
  throw new Error(`Did not reach expected focus target within ${maxTabs} tabs`)
}

async function activeMatches(
  page: Page,
  selector: string,
): Promise<boolean> {
  return page.locator(selector).evaluate((el) => el === document.activeElement)
}

test('production same-origin responsive smoke', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  await mockLoginSuccess(page)
  await mockTicketsSuccess(page)

  await loginThroughUi(page)

  expect(telemetry.requestUrls).toContain(LOGIN_API)
  expect(
    telemetry.requestUrls.some((url) => url.includes('undefined')),
  ).toBe(false)
  expect(
    telemetry.requestUrls.some((url) => url.includes(TICKETS_API)),
  ).toBe(true)

  const statusBadge = visibleResultsRoot(page)
    .locator('.ticket-status-badge')
    .filter({ hasText: 'Escalated' })
  await expect(statusBadge).toBeVisible()
  await assertFieldInsideViewport(page, ticketFixtures[0].subject)
  await assertFieldInsideViewport(page, ticketFixtures[0].customerEmail)
  await assertNoDocumentOverflow(page)

  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  const tableView = page.locator('.ticket-table-view')
  const cardList = page.locator('.ticket-card-list')

  // Breakpoint in tickets.css: max-width 47.99rem (~767.84px at 16px root).
  if (viewport!.width > 767.84) {
    await expect(tableView).toBeVisible()
    await expect(cardList).toBeHidden()
  } else {
    await expect(cardList).toBeVisible()
    await expect(tableView).toBeHidden()
  }

  await testInfo.attach(`portal-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })

  telemetry.assertClean()
})

test('invalid login stays in place and focuses the password', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)

  await page.route(LOGIN_API, async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Unauthorized' }),
    })
  })

  await page.goto('/login')
  await page.getByLabel('Kullanıcı adı').fill('ada.destek')
  await page.getByLabel('Parola').fill('wrong-password')
  await page.getByRole('button', { name: 'Giriş yap' }).click()

  await expect(page.getByRole('alert')).toHaveText(
    'Kullanıcı adı veya parola hatalı.',
  )
  await expect(page.getByLabel('Parola')).toBeFocused()
  await expect(page).toHaveURL(/\/login$/)
  await assertNoDocumentOverflow(page)
  // Intentional 401 is asserted via UI copy; browser also logs the resource status.
  telemetry.assertClean({ allowUnauthorizedConsole: true })
})

test('protected 401 clears session and explains expiry', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)

  await page.route(TICKETS_API, async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Unauthorized' }),
    })
  })

  await page.goto('/login')
  await page.evaluate(() => {
    sessionStorage.setItem('vshd.accessToken', 'stale-token')
    sessionStorage.setItem(
      'vshd.user',
      JSON.stringify({
        userId: '11111111-1111-1111-1111-111111111111',
        fullName: 'Ada Destek',
        username: 'ada.destek',
      }),
    )
  })
  await page.goto('/tickets')

  await expect(page.getByRole('status')).toHaveText(EXPIRY_NOTICE)
  await expect(page).toHaveURL(/\/login$/)
  expect(new URL(page.url()).search).toBe('')

  const remaining = await page.evaluate(() => ({
    token: sessionStorage.getItem('vshd.accessToken'),
    user: sessionStorage.getItem('vshd.user'),
  }))
  expect(remaining.token).toBeNull()
  expect(remaining.user).toBeNull()

  await assertNoDocumentOverflow(page)
  telemetry.assertClean({ allowUnauthorizedConsole: true })
})

test('keyboard focus and controls work without a pointer', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  await mockLoginSuccess(page)
  await mockTicketsSuccess(page)

  await page.goto('/login')
  await page.locator('body').focus()

  await page.keyboard.press('Tab')
  await tabUntil(page, () => activeMatches(page, '.skip-link'))
  await expect(page.locator('.skip-link')).toBeFocused()

  await tabUntil(page, () => activeMatches(page, 'input[name="username"]'))
  await expect(page.getByLabel('Kullanıcı adı')).toBeFocused()
  await page.keyboard.type('ada.destek')

  await page.keyboard.press('Tab')
  await expect(page.getByLabel('Parola')).toBeFocused()
  await page.keyboard.type('secret-password')

  await page.keyboard.press('Tab')
  await expect(
    page.getByRole('button', { name: 'Giriş yap' }),
  ).toBeFocused()
  await assertFocusVisibleOutline(page)
  await page.keyboard.press('Enter')

  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()
  await page.locator('body').focus()

  await page.keyboard.press('Tab')
  await tabUntil(page, () => activeMatches(page, '.skip-link'))
  await expect(page.locator('.skip-link')).toBeFocused()

  // DOM order: search → status select → Yenile → lifecycle segments.
  await tabUntil(page, () => activeMatches(page, '#ticket-search'))
  await expect(page.locator('#ticket-search')).toBeFocused()

  await page.keyboard.press('Tab')
  await expect(page.locator('#ticket-status')).toBeFocused()

  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Yenile' })).toBeFocused()
  await assertFocusVisibleOutline(page)
  await page.keyboard.press('Enter')
  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()
  // Refresh may re-render the control label; re-establish keyboard focus trail.
  const lifecycleAll = page.locator(
    '.ticket-lifecycle__segment[data-status="all"]',
  )
  const lifecycleNew = page.locator(
    '.ticket-lifecycle__segment[data-status="New"]',
  )
  await page.locator('body').focus()
  await tabUntil(page, () =>
    lifecycleAll.evaluate((el) => el === document.activeElement),
  )
  await expect(lifecycleAll).toBeFocused()
  await assertFocusVisibleOutline(page)
  await page.keyboard.press('Enter')
  await expect(lifecycleAll).toHaveAttribute('aria-pressed', 'true')

  await page.keyboard.press('Tab')
  await expect(lifecycleNew).toBeFocused()
  await page.keyboard.press(' ')
  await expect(lifecycleNew).toHaveAttribute('aria-pressed', 'true')
  await assertFocusVisibleOutline(page)

  // Logout lives in the header (before main controls in tab order).
  await page.locator('body').focus()
  await tabUntil(page, () =>
    page
      .getByRole('button', { name: 'Çıkış yap' })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(
    page.getByRole('button', { name: 'Çıkış yap' }),
  ).toBeFocused()
  await assertFocusVisibleOutline(page)
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/login/)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('reduced motion disables the coordinated entry animation', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  await page.emulateMedia({ reducedMotion: 'reduce' })

  await page.goto('/login')
  await expect(page.locator('.portal-enter')).toBeVisible()

  const animation = await page.locator('.portal-enter').evaluate((el) => {
    const style = getComputedStyle(el)
    return {
      duration: style.animationDuration,
      name: style.animationName,
    }
  })

  // prefers-reduced-motion sets animation-duration: 0.01ms
  const durationSeconds = animation.duration
    .split(',')
    .map((part) => part.trim())
    .map((part) => {
      if (part.endsWith('ms')) {
        return parseFloat(part) / 1000
      }
      if (part.endsWith('s')) {
        return parseFloat(part)
      }
      return Number.POSITIVE_INFINITY
    })
  expect(Math.max(...durationSeconds)).toBeLessThan(0.05)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})
