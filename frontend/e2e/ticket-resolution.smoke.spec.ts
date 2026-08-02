import {
  expect,
  test,
  type Page,
  type Route,
  type TestInfo,
} from '@playwright/test'
import type {
  ResolveTicketResult,
  TicketDetails,
  TicketListItem,
} from '../src/api/types'

const ORIGIN = 'http://127.0.0.1:4173'
const LOGIN_API = `${ORIGIN}/api/auth/login`
const TICKETS_API = `${ORIGIN}/api/tickets`
// List reads are cursor-paginated (?pageSize=50&search&status&cursor).
const TICKETS_LIST_API_PATTERN = /\/api\/tickets(\?.*)?$/

function pagedListPage(items: TicketListItem[]) {
  return {
    items,
    nextCursor: null,
    hasMore: false,
    counts: {
      all: items.length,
      new: items.filter(({ status }) => status === 'New').length,
      waitingCustomerReply: items.filter(
        ({ status }) => status === 'WaitingCustomerReply',
      ).length,
      customerReplied: items.filter(
        ({ status }) => status === 'CustomerReplied',
      ).length,
      resolved: items.filter(({ status }) => status === 'Resolved').length,
    },
  }
}

const TICKET_ID = '22222222-2222-2222-2222-222222222222'
const MESSAGE_CUSTOMER_ID = '33333333-3333-3333-3333-333333333333'
const MESSAGE_SUPPORT_ID = '44444444-4444-4444-4444-444444444444'
const MESSAGE_REOPEN_ID = '77777777-7777-7777-7777-777777777777'
const ATTACHMENT_ID = '66666666-6666-6666-6666-666666666666'
const ATTACHMENT_FILE_NAME = 'ekran-goruntusu.png'
const CLOSER_USER_ID = '11111111-1111-1111-1111-111111111111'

const RESOLVE_AT = '2026-08-10T10:00:00Z'
const RESOLVE_AT_ISO = '2026-08-10T10:00:00.000Z'

const COPY = {
  trigger: 'Çözüldü olarak işaretle',
  dialogTitle: 'Talebi çözmek istiyor musunuz?',
  dialogDescription: 'Müşterinin yeni bir e-postası bu talebi yeniden açar.',
  cancel: 'Vazgeç',
  confirm: 'Talebi çöz',
  resolved: 'Talep çözüldü.',
  alreadyResolved: 'Talep zaten çözülmüş. Güncel bilgiler yüklendi.',
  conflict: 'Talep başka bir işlemle güncellendi. Güncel durum yüklendi.',
  serverError: 'Talep çözülemedi. Lütfen yeniden deneyin.',
  closureNote:
    'Bu talep çözüldü. Müşteri yeni bir e-posta gönderirse talep yeniden açılır.',
  refreshFailed: 'Talep ayrıntıları yüklenemedi. Lütfen yeniden deneyin.',
} as const

const LONG_SUBJECT =
  'Uzak ofis yazıcısında tekrar eden ve ayrıntılı inceleme gerektiren bağlantı sorunu — ' +
  'çok-uzun-tek-satır-konu-alanı-'.repeat(6)
const LONG_MESSAGE =
  'Müşteri mesaj gövdesi: bağlantı koptu, kuyruk birikti, yazıcı çevrimdışı kaldı. ' +
  'Uzun-gövde-metni-'.repeat(40)
const LONG_FILE_NAME =
  'cok-uzun-ek-dosya-adi-uzaktan-ofis-yazici-sorun-raporu-ve-ekran-goruntusu-'.repeat(
    2,
  ) + '.pdf'
const HTML_LOOKING_CONTENT =
  '<img onerror="alert(1)" src=x><strong>kalın</strong> literal içerik'

type ResolveMode = 'success' | 'already-resolved' | 'conflict' | 'server-error'

type MutableTicketFixture = {
  listItem: TicketListItem
  detail: TicketDetails
  resolveMode: ResolveMode
  resolvePostCount: number
  /** Authorization header if any (cookie era: should stay null). */
  lastResolveAuth: string | null
  lastResolveCsrf: string | null
  lastResolveBody: string | null
  /** Fail the next detail GET only after at least one resolve POST (refresh path). */
  failNextDetailGetAfterResolve: boolean
}

const SEED_USER = {
  userId: CLOSER_USER_ID,
  fullName: 'Ada Destek',
  username: 'ada.destek',
  role: 'Support',
}

const ME_API = `${ORIGIN}/api/auth/me`

function cloneDetail(detail: TicketDetails): TicketDetails {
  return structuredClone(detail)
}

function createFixture(options?: {
  longContent?: boolean
  status?: string
  resolved?: boolean
}): MutableTicketFixture {
  const longContent = options?.longContent ?? false
  const resolved = options?.resolved ?? options?.status === 'Resolved'
  const status = options?.status ?? (resolved ? 'Resolved' : 'CustomerReplied')
  const subject = longContent ? LONG_SUBJECT : 'Yazıcı bağlantı sorunu'
  const customerName = longContent
    ? 'İrem Uzunsoyad-'.repeat(4)
    : 'İrem Uzunsoyad'
  const customerEmail = longContent
    ? 'irem.uzun.birim.adi.cok.uzun.adres@example.test'
    : 'irem.uzun@example.test'
  const fileName = longContent ? LONG_FILE_NAME : ATTACHMENT_FILE_NAME
  const customerContent = longContent ? LONG_MESSAGE : HTML_LOOKING_CONTENT

  const detail: TicketDetails = {
    id: TICKET_ID,
    ticketNumber: 'VS-000042',
    subject,
    customerName,
    customerEmail,
    status,
    assignedUserId: null,
    createdAt: '2026-07-20T09:00:00.000Z',
    updatedAt: resolved ? RESOLVE_AT_ISO : '2026-07-20T11:00:00.000Z',
    lastActivityAt: resolved ? RESOLVE_AT_ISO : '2026-07-20T11:00:00.000Z',
    waitingCustomerSince: null,
    resolvedAt: resolved ? RESOLVE_AT_ISO : null,
    closedByUserId: resolved ? CLOSER_USER_ID : null,
    messages: [
      {
        id: MESSAGE_CUSTOMER_ID,
        senderType: 'Customer',
        userId: null,
        content: customerContent,
        isHtml: true,
        createdAt: '2026-07-20T09:05:00.000Z',
      },
      {
        id: MESSAGE_SUPPORT_ID,
        senderType: 'Support',
        userId: CLOSER_USER_ID,
        content: 'Merhaba, yazıcı kuyruğunu kontrol ediyoruz.',
        isHtml: false,
        createdAt: '2026-07-20T10:00:00.000Z',
      },
    ],
    attachments: [
      {
        id: ATTACHMENT_ID,
        ticketMessageId: MESSAGE_CUSTOMER_ID,
        fileName,
        contentType: longContent ? 'application/pdf' : 'image/png',
        fileSize: longContent ? 204_800 : 4096,
        createdAt: '2026-07-20T09:05:01.000Z',
      },
    ],
  }

  return {
    listItem: {
      id: detail.id,
      ticketNumber: detail.ticketNumber,
      subject: detail.subject,
      customerName: detail.customerName,
      customerEmail: detail.customerEmail,
      status: detail.status,
      lastActivityAt: detail.lastActivityAt,
      assignedUserId: detail.assignedUserId,
    },
    detail,
    resolveMode: 'success',
    resolvePostCount: 0,
    lastResolveAuth: null,
    lastResolveCsrf: null,
    lastResolveBody: null,
    failNextDetailGetAfterResolve: false,
  }
}

function applyResolvedState(fixture: MutableTicketFixture): ResolveTicketResult {
  const patch = {
    status: 'Resolved' as const,
    resolvedAt: RESOLVE_AT,
    updatedAt: RESOLVE_AT,
    lastActivityAt: RESOLVE_AT,
    waitingCustomerSince: null as string | null,
    closedByUserId: CLOSER_USER_ID,
  }

  fixture.detail = {
    ...fixture.detail,
    status: patch.status,
    resolvedAt: RESOLVE_AT_ISO,
    updatedAt: RESOLVE_AT_ISO,
    lastActivityAt: RESOLVE_AT_ISO,
    waitingCustomerSince: null,
    closedByUserId: CLOSER_USER_ID,
  }
  fixture.listItem = {
    ...fixture.listItem,
    status: fixture.detail.status,
    lastActivityAt: fixture.detail.lastActivityAt,
  }

  return {
    ticketId: TICKET_ID,
    ticketNumber: fixture.detail.ticketNumber,
    status: 'Resolved',
    resolvedAt: RESOLVE_AT,
    updatedAt: RESOLVE_AT,
    lastActivityAt: RESOLVE_AT,
    closedByUserId: CLOSER_USER_ID,
    changed: true,
  }
}

function applyAlreadyResolvedResponse(
  fixture: MutableTicketFixture,
): ResolveTicketResult {
  fixture.detail = {
    ...fixture.detail,
    status: 'Resolved',
    resolvedAt: RESOLVE_AT_ISO,
    updatedAt: RESOLVE_AT_ISO,
    lastActivityAt: RESOLVE_AT_ISO,
    waitingCustomerSince: null,
    closedByUserId: CLOSER_USER_ID,
  }
  fixture.listItem = {
    ...fixture.listItem,
    status: 'Resolved',
    lastActivityAt: RESOLVE_AT_ISO,
  }

  return {
    ticketId: TICKET_ID,
    ticketNumber: fixture.detail.ticketNumber,
    status: 'Resolved',
    resolvedAt: RESOLVE_AT,
    updatedAt: RESOLVE_AT,
    lastActivityAt: RESOLVE_AT,
    closedByUserId: CLOSER_USER_ID,
    changed: false,
  }
}

function mutateToCustomerRepliedReopen(fixture: MutableTicketFixture): void {
  const reopenAt = '2026-08-10T11:30:00.000Z'
  fixture.detail = {
    ...fixture.detail,
    status: 'CustomerReplied',
    resolvedAt: null,
    closedByUserId: null,
    waitingCustomerSince: null,
    updatedAt: reopenAt,
    lastActivityAt: reopenAt,
    messages: [
      ...fixture.detail.messages,
      {
        id: MESSAGE_REOPEN_ID,
        senderType: 'Customer',
        userId: null,
        content: 'Tekrar yazıyorum, sorun devam ediyor.',
        isHtml: false,
        createdAt: reopenAt,
      },
    ],
  }
  fixture.listItem = {
    ...fixture.listItem,
    status: 'CustomerReplied',
    lastActivityAt: reopenAt,
  }
}

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
    if (
      failure.includes('ERR_ABORTED') ||
      failure.includes('NS_BINDING_ABORTED')
    ) {
      return
    }
    failedRequests.push(`${request.method()} ${request.url()} ${failure}`)
  })
  page.on('request', (request) => {
    requestUrls.push(request.url())
  })

  return {
    consoleErrors,
    pageErrors,
    failedRequests,
    requestUrls,
    assertClean(options?: {
      allowConflictConsole?: boolean
      allowServerErrorConsole?: boolean
    }) {
      // Cookie-era bootstrap probes /api/auth/me; 401 is expected when unauthenticated.
      let filteredConsole = consoleErrors.filter(
        (text) =>
          !/Failed to load resource:.*401/.test(text) &&
          !/401 \(Unauthorized\)/.test(text),
      )
      if (options?.allowConflictConsole) {
        filteredConsole = filteredConsole.filter(
          (text) =>
            !/Failed to load resource:.*409/.test(text) &&
            !/409 \(Conflict\)/.test(text),
        )
      }
      if (options?.allowServerErrorConsole) {
        filteredConsole = filteredConsole.filter(
          (text) =>
            !/Failed to load resource:.*500/.test(text) &&
            !/500 \(Internal Server Error\)/.test(text),
        )
      }
      expect(filteredConsole, filteredConsole.join('\n')).toEqual([])
      expect(pageErrors, pageErrors.join('\n')).toEqual([])
      expect(failedRequests, failedRequests.join('\n')).toEqual([])
    },
  }
}

async function assertNoDocumentOverflow(page: Page) {
  const overflow = await page.evaluate(() => ({
    document:
      document.documentElement.scrollWidth -
      document.documentElement.clientWidth,
    body: document.body.scrollWidth - document.body.clientWidth,
  }))
  expect(overflow.document).toBeLessThanOrEqual(0)
  expect(overflow.body).toBeLessThanOrEqual(0)
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
  maxTabs = 40,
): Promise<void> {
  for (let i = 0; i < maxTabs; i += 1) {
    if (await isMatch()) {
      return
    }
    await page.keyboard.press('Tab')
  }
  throw new Error(`Did not reach expected focus target within ${maxTabs} tabs`)
}

async function mockMeUnauthorized(page: Page) {
  await page.route(ME_API, async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Unauthorized' }),
    })
  })
}

async function mockMeSuccess(page: Page) {
  await page.route(ME_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(SEED_USER),
    })
  })
}

async function seedAuthenticatedSession(page: Page) {
  // Last registered route wins — overrides install*Mocks unauthorized /me.
  await mockMeSuccess(page)
  await page.goto('/login')
  await page.evaluate((user) => {
    sessionStorage.removeItem('vshd.accessToken') // legacy Bearer key
    sessionStorage.setItem('vshd.user', JSON.stringify(user))
    document.cookie = 'vshd.csrf=browser-test-csrf; Path=/'
  }, SEED_USER)
}

async function installResolutionMocks(
  page: Page,
  fixture: MutableTicketFixture,
) {
  // Default unauthenticated bootstrap so loginThroughUi can show the form.
  await mockMeUnauthorized(page)
  await page.route(`${ORIGIN}/api/auth/logout`, async (route) => {
    await route.fulfill({ status: 204, body: '' })
  })
  await page.route(LOGIN_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: {
        'Set-Cookie': 'vshd.csrf=browser-test-csrf; Path=/',
      },
      body: JSON.stringify(SEED_USER),
    })
  })

  await page.route(TICKETS_LIST_API_PATTERN, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedListPage([fixture.listItem])),
    })
  })

  const detailUrl = `${TICKETS_API}/${TICKET_ID}`
  const resolveUrl = `${detailUrl}/resolve`
  const assigneesUrl = `${TICKETS_API}/assignees`
  const attachmentUrl = `${ORIGIN}/api/attachments/${ATTACHMENT_ID}`

  await page.route(assigneesUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: SEED_USER.userId,
          fullName: SEED_USER.fullName,
          username: SEED_USER.username,
        },
      ]),
    })
  })

  await page.route(resolveUrl, async (route: Route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }

    const request = route.request()
    fixture.resolvePostCount += 1
    fixture.lastResolveAuth = request.headers().authorization ?? null
    fixture.lastResolveCsrf = request.headers()['x-csrf-token'] ?? null
    fixture.lastResolveBody = request.postData()
    // Cookie-era SPA: credentials + CSRF header; no Authorization Bearer.
    expect(fixture.lastResolveAuth).toBeNull()
    expect(fixture.lastResolveCsrf).toBeTruthy()
    expect(fixture.lastResolveBody).toBeNull()

    if (fixture.resolveMode === 'conflict') {
      mutateToCustomerRepliedReopen(fixture)
      await route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({
          status: 409,
          title: 'The request conflicts with current state.',
        }),
      })
      return
    }

    if (fixture.resolveMode === 'server-error') {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'raw-server-error' }),
      })
      return
    }

    if (fixture.resolveMode === 'already-resolved') {
      const result = applyAlreadyResolvedResponse(fixture)
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(result),
      })
      return
    }

    const result = applyResolvedState(fixture)
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(result),
    })
  })

  await page.route(detailUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    if (
      fixture.failNextDetailGetAfterResolve &&
      fixture.resolvePostCount > 0
    ) {
      fixture.failNextDetailGetAfterResolve = false
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'raw-refresh-failure' }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(cloneDetail(fixture.detail)),
    })
  })

  await page.route(attachmentUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    const bytes = Buffer.from('fake-png-bytes-for-browser-download')
    await route.fulfill({
      status: 200,
      headers: {
        'content-type': 'image/png',
        'content-disposition': `attachment; filename="${ATTACHMENT_FILE_NAME}"`,
      },
      body: bytes,
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
  // Full reloads re-run AuthProvider bootstrap — restore session via /me.
  await mockMeSuccess(page)
}

async function openTicketFromList(page: Page) {
  const visibleRoot = page.locator(
    '.ticket-table-view:visible, .ticket-card-list:visible',
  )
  await visibleRoot
    .getByRole('link', { name: /VS-000042/ })
    .first()
    .click()
  await expect(page).toHaveURL(new RegExp(`/tickets/${TICKET_ID}$`))
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

test('login list detail confirm resolve shows resolved status and notice', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.resolveMode = 'success'
  await installResolutionMocks(page, fixture)

  await loginThroughUi(page)
  await openTicketFromList(page)

  await expect(page.getByText('Müşteri Yanıtladı')).toBeVisible()
  const trigger = page.getByRole('button', { name: COPY.trigger })
  await expect(trigger).toBeVisible()
  await expect(trigger).toHaveText(COPY.trigger)
  await trigger.click()

  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  await expect(dialog).toHaveAccessibleName(COPY.dialogTitle)
  await expect(dialog).toHaveAccessibleDescription(COPY.dialogDescription)
  await expect(page.getByRole('button', { name: COPY.cancel })).toBeFocused()
  await expect(page.getByRole('button', { name: COPY.cancel })).toHaveText(
    COPY.cancel,
  )
  await expect(page.getByRole('button', { name: COPY.confirm })).toHaveText(
    COPY.confirm,
  )

  await page.getByRole('button', { name: COPY.confirm }).click()

  const notice = page.getByRole('status').filter({ hasText: COPY.resolved })
  await expect(notice).toBeVisible()
  await expect(notice).toBeFocused()

  await expect(page.getByText('Çözüldü').first()).toBeVisible()
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)
  await expect(page.getByRole('button', { name: COPY.trigger })).toHaveCount(0)
  expect(fixture.resolvePostCount).toBe(1)
  expect(fixture.lastResolveBody).toBeNull()
  expect(fixture.lastResolveAuth).toBeNull()
  expect(fixture.lastResolveCsrf).toBe('browser-test-csrf')

  // History + attachment remain usable after resolve.
  await expect(
    page.getByRole('heading', { name: 'Mesaj geçmişi' }),
  ).toBeVisible()
  await expect(page.getByText(HTML_LOOKING_CONTENT)).toBeVisible()
  await expect(
    page.getByRole('button', {
      name: `${ATTACHMENT_FILE_NAME} dosyasını indir`,
    }),
  ).toBeEnabled()

  await assertNoDocumentOverflow(page)
  await testInfo.attach(`ticket-resolution-resolved-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })

  // Browser back remains usable.
  await page.goBack()
  await expect(page).toHaveURL(/\/tickets$/)
  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()

  // Re-open detail stays resolved; logout still works.
  await openTicketFromList(page)
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await page.getByRole('button', { name: 'Yenile' }).click()
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await page.getByRole('button', { name: 'Çıkış yap' }).click()
  await expect(page).toHaveURL(/\/login$/)

  telemetry.assertClean()
})

test('resolved initial direct load hides composer and resolve action', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ resolved: true })
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await expect(page.getByText('Çözüldü').first()).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)
  await expect(page.getByRole('button', { name: COPY.trigger })).toHaveCount(0)
  await expect(
    page.getByRole('heading', { name: 'Mesaj geçmişi' }),
  ).toBeVisible()
  await expect(
    page.getByRole('button', {
      name: `${ATTACHMENT_FILE_NAME} dosyasını indir`,
    }),
  ).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Yenile' })).toBeEnabled()

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('already-resolved response shows no-op notice and posts once', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.resolveMode = 'already-resolved'
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await page.getByRole('button', { name: COPY.trigger }).click()
  await page.getByRole('button', { name: COPY.confirm }).click()

  await expect(
    page.getByRole('status').filter({ hasText: COPY.alreadyResolved }),
  ).toBeVisible()
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  expect(fixture.resolvePostCount).toBe(1)
  expect(fixture.lastResolveBody).toBeNull()

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('409 conflict refreshes once to CustomerReplied without second POST', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.resolveMode = 'conflict'
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toBeVisible()

  // Remove reopen message until conflict mutates fixture (mutate adds one).
  const initialMessageCount = fixture.detail.messages.length

  await page.getByRole('button', { name: COPY.trigger }).click()
  await page.getByRole('button', { name: COPY.confirm }).click()

  await expect(
    page.getByRole('alert').filter({ hasText: COPY.conflict }),
  ).toBeVisible()
  await expect(page.getByText('Müşteri Yanıtladı')).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toBeVisible()
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeVisible()
  await expect(page.getByText('Tekrar yazıyorum, sorun devam ediyor.')).toBeVisible()
  expect(fixture.resolvePostCount).toBe(1)
  expect(fixture.detail.messages.length).toBe(initialMessageCount + 1)

  // UI must not auto-retry POST.
  await page.waitForTimeout(300)
  expect(fixture.resolvePostCount).toBe(1)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean({ allowConflictConsole: true })
})

test('server-confirmed resolve with refresh 5xx keeps resolved controls hidden', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.resolveMode = 'success'
  // Fail only the post-resolve refresh GET, not the initial detail load.
  fixture.failNextDetailGetAfterResolve = true
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeVisible()
  await page.getByRole('button', { name: COPY.trigger }).click()
  await page.getByRole('button', { name: COPY.confirm }).click()

  await expect(
    page.getByRole('status').filter({ hasText: COPY.resolved }),
  ).toBeVisible()
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await expect(page.getByText('Çözüldü').first()).toBeVisible()
  await expect(
    page.getByRole('button', { name: COPY.trigger }),
  ).toHaveCount(0)
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)
  await expect(page.getByText(COPY.refreshFailed)).toBeVisible()
  // Two notices coexist: resolve success (status) + refresh failure (alert).
  await expect(
    page.getByRole('status').filter({ hasText: COPY.resolved }),
  ).toBeVisible()
  await expect(
    page.getByRole('alert').filter({ hasText: COPY.refreshFailed }),
  ).toBeVisible()
  expect(fixture.resolvePostCount).toBe(1)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean({ allowServerErrorConsole: true })
})

test('refresh after reopen fixture restores composer resolve action and message', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ resolved: true })
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)

  mutateToCustomerRepliedReopen(fixture)
  await page.getByRole('button', { name: 'Yenile' }).click()

  await expect(page.getByText('Müşteri Yanıtladı')).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toBeVisible()
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeVisible()
  await expect(page.getByText(COPY.closureNote)).toHaveCount(0)
  await expect(
    page.getByText('Tekrar yazıyorum, sorun devam ediyor.'),
  ).toBeVisible()

  const timelineItems = page.locator('.ticket-timeline__item')
  await expect(timelineItems).toHaveCount(3)
  await expect(timelineItems.nth(2)).toContainText(
    'Tekrar yazıyorum, sorun devam ediyor.',
  )
  await expect(timelineItems.nth(2).locator('time')).toHaveAttribute(
    'dateTime',
    '2026-08-10T11:30:00.000Z',
  )

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('keyboard dialog focus Escape confirm and result focus', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.resolveMode = 'success'
  await installResolutionMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeVisible()

  await page.locator('body').focus()
  await tabUntil(page, () =>
    page
      .getByRole('button', { name: COPY.trigger })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeFocused()
  await assertFocusVisibleOutline(page)

  await page.keyboard.press('Enter')
  await expect(page.getByRole('alertdialog')).toBeVisible()
  await expect(page.getByRole('button', { name: COPY.cancel })).toBeFocused()
  await assertFocusVisibleOutline(page)

  await page.keyboard.press('Escape')
  await expect(page.getByRole('alertdialog')).toHaveCount(0)
  await expect(page.getByRole('button', { name: COPY.trigger })).toBeFocused()

  await page.keyboard.press('Enter')
  await expect(page.getByRole('button', { name: COPY.cancel })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: COPY.confirm })).toBeFocused()
  await assertFocusVisibleOutline(page)
  await page.keyboard.press('Enter')

  const notice = page.getByRole('status').filter({ hasText: COPY.resolved })
  await expect(notice).toBeVisible()
  await expect(notice).toBeFocused()
  // Result notice is focused programmatically (tabIndex=-1); outline is asserted
  // on keyboard-reached trigger/dialog controls above.
  expect(fixture.resolvePostCount).toBe(1)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('long content reduced motion keeps zero document overflow when resolved', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ longContent: true, resolved: true })
  await installResolutionMocks(page, fixture)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(LONG_SUBJECT)
  await expect(page.getByText(LONG_MESSAGE)).toBeVisible()
  await expect(page.getByText(COPY.closureNote)).toBeVisible()
  await expect(
    page.getByRole('button', { name: `${LONG_FILE_NAME} dosyasını indir` }),
  ).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)

  const animation = await page.locator('.ticket-detail').evaluate((el) => {
    const style = getComputedStyle(el)
    return {
      duration: style.animationDuration,
      name: style.animationName,
    }
  })
  expect(
    animation.name === 'none' ||
      animation.duration
        .split(',')
        .map((part) => part.trim())
        .every((part) => {
          if (part === '0s' || part === '0ms') {
            return true
          }
          if (part.endsWith('ms')) {
            return parseFloat(part) / 1000 < 0.05
          }
          if (part.endsWith('s')) {
            return parseFloat(part) < 0.05
          }
          return false
        }),
  ).toBe(true)

  await assertNoDocumentOverflow(page)
  await testInfo.attach(
    `ticket-resolution-long-${testInfo.project.name}`,
    {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    },
  )
  telemetry.assertClean()
})
