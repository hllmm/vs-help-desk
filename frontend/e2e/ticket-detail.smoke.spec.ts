import {
  expect,
  test,
  type Page,
  type Route,
  type TestInfo,
} from '@playwright/test'
import type {
  AssignableUser,
  SupportReplyResult,
  TicketDetails,
  TicketListItem,
} from '../src/api/types'

const ORIGIN = 'http://127.0.0.1:4173'
const LOGIN_API = `${ORIGIN}/api/auth/login`
const TICKETS_API = `${ORIGIN}/api/tickets`

const TICKET_ID = '22222222-2222-2222-2222-222222222222'
const UNKNOWN_TICKET_ID = '99999999-9999-9999-9999-999999999999'
const MESSAGE_CUSTOMER_ID = '33333333-3333-3333-3333-333333333333'
const MESSAGE_SUPPORT_ID = '44444444-4444-4444-4444-444444444444'
const MESSAGE_REPLY_ID = '55555555-5555-5555-5555-555555555555'
const ATTACHMENT_ID = '66666666-6666-6666-6666-666666666666'
const ASSIGNEE_ADMIN_ID = '77777777-7777-7777-7777-777777777777'
const ATTACHMENT_FILE_NAME = 'ekran-goruntusu.png'

const EXPIRY_NOTICE =
  'Oturumunuz sona erdi. Devam etmek için yeniden giriş yapın.'
const DELIVERED_NOTICE = 'Yanıt kaydedildi ve müşteriye gönderildi.'
const SMTP_FAILED_NOTICE =
  'Yanıt kaydedildi ancak e-posta müşteriye gönderilemedi.'
const NOT_FOUND_NOTICE = 'Destek talebi bulunamadı.'

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

type ReplyMode = 'delivered' | 'smtp-failed'

type MutableTicketFixture = {
  listItem: TicketListItem
  detail: TicketDetails
  replyMode: ReplyMode
  lastReplyBody: unknown
  lastAssignmentBody: unknown
  lastAssignmentCsrf: string | null
  /** Authorization header if any (cookie era: should stay null). */
  lastAttachmentAuth: string | null
  lastAttachmentUrl: string | null
}

const SEED_USER = {
  userId: '11111111-1111-1111-1111-111111111111',
  fullName: 'Ada Destek',
  username: 'ada.destek',
  role: 'Support',
}

const ASSIGNEES: AssignableUser[] = [
  {
    id: SEED_USER.userId,
    fullName: SEED_USER.fullName,
    username: SEED_USER.username,
  },
  {
    id: ASSIGNEE_ADMIN_ID,
    fullName: 'Ece Yönetici',
    username: 'ece.admin',
  },
]

const ME_API = `${ORIGIN}/api/auth/me`

function cloneDetail(detail: TicketDetails): TicketDetails {
  return structuredClone(detail)
}

function createFixture(options?: {
  longContent?: boolean
  status?: string
}): MutableTicketFixture {
  const longContent = options?.longContent ?? false
  const status = options?.status ?? 'CustomerReplied'
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
    updatedAt: '2026-07-20T11:00:00.000Z',
    lastActivityAt: '2026-07-20T11:00:00.000Z',
    waitingCustomerSince: null,
    resolvedAt: null,
    closedByUserId: null,
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
        userId: '11111111-1111-1111-1111-111111111111',
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
    replyMode: 'delivered',
    lastReplyBody: null,
    lastAssignmentBody: null,
    lastAssignmentCsrf: null,
    lastAttachmentAuth: null,
    lastAttachmentUrl: null,
  }
}

function applySavedReply(
  fixture: MutableTicketFixture,
  content: string,
  mode: ReplyMode,
): SupportReplyResult {
  const now = '2026-07-20T12:00:00.000Z'
  const nextStatus =
    mode === 'delivered' ? 'WaitingCustomerReply' : fixture.detail.status

  fixture.detail = {
    ...fixture.detail,
    status: nextStatus,
    updatedAt: now,
    lastActivityAt: now,
    waitingCustomerSince: mode === 'delivered' ? now : fixture.detail.waitingCustomerSince,
    messages: [
      ...fixture.detail.messages,
      {
        id: MESSAGE_REPLY_ID,
        senderType: 'Support',
        userId: '11111111-1111-1111-1111-111111111111',
        content,
        isHtml: false,
        createdAt: now,
      },
    ],
  }
  fixture.listItem = {
    ...fixture.listItem,
    status: fixture.detail.status,
    lastActivityAt: fixture.detail.lastActivityAt,
  }

  if (mode === 'delivered') {
    return {
      ticketId: TICKET_ID,
      ticketNumber: fixture.detail.ticketNumber,
      messageId: MESSAGE_REPLY_ID,
      status: 'WaitingCustomerReply',
      emailDelivered: true,
      ticketStateUpdated: true,
      noticeCode: null,
    }
  }

  return {
    ticketId: TICKET_ID,
    ticketNumber: fixture.detail.ticketNumber,
    messageId: MESSAGE_REPLY_ID,
    status: fixture.detail.status,
    emailDelivered: false,
    ticketStateUpdated: false,
    noticeCode: 'smtp-delivery-failed',
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
      allowUnauthorizedConsole?: boolean
      allowNotFoundConsole?: boolean
    }) {
      // Default: filter expected unauthenticated /me (and other) 401 console noise.
      let filteredConsole =
        options?.allowUnauthorizedConsole === false
          ? consoleErrors
          : consoleErrors.filter(
              (text) =>
                !/Failed to load resource:.*401/.test(text) &&
                !/401 \(Unauthorized\)/.test(text),
            )
      if (options?.allowNotFoundConsole) {
        filteredConsole = filteredConsole.filter(
          (text) =>
            !/Failed to load resource:.*404/.test(text) &&
            !/404 \(Not Found\)/.test(text),
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

async function activeMatches(page: Page, selector: string): Promise<boolean> {
  return page.locator(selector).evaluate((el) => el === document.activeElement)
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

async function mockLoginSuccess(page: Page) {
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
}

async function installTicketWorkspaceMocks(
  page: Page,
  fixture: MutableTicketFixture,
  options?: { detailStatus?: number; unknownTicket404?: boolean },
) {
  // Default unauthenticated bootstrap so loginThroughUi can show the form.
  await mockMeUnauthorized(page)
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

  await page.route(TICKETS_API, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([fixture.listItem]),
    })
  })

  const detailUrl = `${TICKETS_API}/${TICKET_ID}`
  const replyUrl = `${detailUrl}/replies`
  const assigneesUrl = `${TICKETS_API}/assignees`
  const assignmentUrl = `${detailUrl}/assignee`
  const unknownUrl = `${TICKETS_API}/${UNKNOWN_TICKET_ID}`
  const attachmentUrl = `${ORIGIN}/api/attachments/${ATTACHMENT_ID}`

  await page.route(replyUrl, async (route: Route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }
    const raw = route.request().postData() ?? '{}'
    const body = JSON.parse(raw) as Record<string, unknown>
    fixture.lastReplyBody = body
    expect(Object.keys(body).sort()).toEqual(['content'])
    expect(body).not.toHaveProperty('isHtml')
    expect(typeof body.content).toBe('string')

    const result = applySavedReply(
      fixture,
      String(body.content),
      fixture.replyMode,
    )
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(result),
    })
  })

  await page.route(assigneesUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(ASSIGNEES),
    })
  })

  await page.route(assignmentUrl, async (route: Route) => {
    if (route.request().method() !== 'PUT') {
      await route.fallback()
      return
    }

    const raw = route.request().postData() ?? '{}'
    const body = JSON.parse(raw) as Record<string, unknown>
    fixture.lastAssignmentBody = body
    fixture.lastAssignmentCsrf =
      route.request().headers()['x-csrf-token'] ?? null
    expect(Object.keys(body)).toEqual(['userId'])
    expect(
      body.userId === null ||
        ASSIGNEES.some((candidate) => candidate.id === body.userId),
    ).toBe(true)

    const assignedUserId = body.userId as string | null
    const changed = fixture.detail.assignedUserId !== assignedUserId
    const updatedAt = changed
      ? '2026-07-20T11:30:00.000Z'
      : fixture.detail.updatedAt
    fixture.detail = {
      ...fixture.detail,
      assignedUserId,
      updatedAt,
    }
    fixture.listItem = {
      ...fixture.listItem,
      assignedUserId,
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ticketId: TICKET_ID,
        assignedUserId,
        updatedAt,
        changed,
      }),
    })
  })

  await page.route(detailUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    if (options?.detailStatus === 401) {
      await route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Unauthorized' }),
      })
      return
    }
    if (options?.detailStatus === 404) {
      await route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Not Found' }),
      })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(cloneDetail(fixture.detail)),
    })
  })

  if (options?.unknownTicket404 !== false) {
    await page.route(unknownUrl, async (route: Route) => {
      if (route.request().method() !== 'GET') {
        await route.fallback()
        return
      }
      await route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Not Found' }),
      })
    })
  }

  await page.route(attachmentUrl, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    const request = route.request()
    fixture.lastAttachmentAuth = request.headers().authorization ?? null
    fixture.lastAttachmentUrl = request.url()
    // Cookie-era SPA: no Authorization Bearer; no token query params.
    expect(fixture.lastAttachmentAuth).toBeNull()
    expect(fixture.lastAttachmentUrl).not.toMatch(/[?&](access_token|token)=/i)

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

async function expectReadyDetail(page: Page, fixture: MutableTicketFixture) {
  await expect(page.getByText(fixture.detail.ticketNumber).first()).toBeVisible()
  await expect(
    page.getByRole('heading', { name: fixture.detail.subject }),
  ).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Mesaj geçmişi' }),
  ).toBeVisible()
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toBeVisible()
}

test('login list detail refresh timeline and browser back', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture)

  await loginThroughUi(page)
  await openTicketFromList(page)
  await expectReadyDetail(page, fixture)

  const timelineItems = page.locator('.ticket-timeline__item')
  await expect(timelineItems).toHaveCount(2)
  await expect(timelineItems.nth(0)).toContainText(HTML_LOOKING_CONTENT)
  await expect(timelineItems.nth(0).locator('img')).toHaveCount(0)
  await expect(timelineItems.nth(0).locator('strong')).toHaveCount(0)
  await expect(timelineItems.nth(1)).toContainText(
    'Merhaba, yazıcı kuyruğunu kontrol ediyoruz.',
  )
  await expect(timelineItems.nth(0).locator('time')).toHaveAttribute(
    'dateTime',
    '2026-07-20T09:05:00.000Z',
  )
  await expect(timelineItems.nth(1).locator('time')).toHaveAttribute(
    'dateTime',
    '2026-07-20T10:00:00.000Z',
  )

  await page.reload()
  await expectReadyDetail(page, fixture)
  await expect(page.getByText(HTML_LOOKING_CONTENT)).toBeVisible()

  await assertNoDocumentOverflow(page)
  await testInfo.attach(`ticket-detail-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })

  await page.goBack()
  await expect(page).toHaveURL(/\/tickets$/)
  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()

  telemetry.assertClean()
})

test('authenticated attachment download uses cookies and suggested filename', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture)
  await mockLoginSuccess(page)
  await seedAuthenticatedSession(page)

  const downloadPromise = page.waitForEvent('download')
  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)

  await page
    .getByRole('button', { name: `${ATTACHMENT_FILE_NAME} dosyasını indir` })
    .click()

  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe(ATTACHMENT_FILE_NAME)
  expect(fixture.lastAttachmentAuth).toBeNull()
  expect(fixture.lastAttachmentUrl).toBe(
    `${ORIGIN}/api/attachments/${ATTACHMENT_ID}`,
  )
  expect(fixture.lastAttachmentUrl).not.toContain('token')

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('successful reply announces delivery and shows waiting status', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.replyMode = 'delivered'
  await installTicketWorkspaceMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)
  await expect(page.getByText('Müşteri Yanıtladı')).toBeVisible()

  const replyText = 'Merhaba, yazıcı sürücüsünü yeniden yükleyin.'
  await page.getByLabel('Yanıtınız').fill(replyText)
  await page.getByRole('button', { name: 'Yanıtı gönder' }).click()

  await expect(
    page.locator('.ticket-reply__notice[role="status"]'),
  ).toHaveText(DELIVERED_NOTICE)
  await expect(page.getByLabel('Yanıtınız')).toHaveValue('')
  await expect(page.getByText(replyText)).toBeVisible()
  await expect(page.getByText('Müşteri Bekleniyor')).toBeVisible()
  expect(fixture.lastReplyBody).toEqual({ content: replyText })

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('assigns and clears an active owner without overflow', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)

  const assigneeSelect = page.getByRole('combobox', {
    name: 'Atanan destek personeli',
  })
  const saveButton = page.getByRole('button', { name: 'Atamayı kaydet' })
  await expect(assigneeSelect).toHaveValue('')
  await expect(saveButton).toBeDisabled()

  await assigneeSelect.selectOption(ASSIGNEE_ADMIN_ID)
  await expect(saveButton).toBeEnabled()
  await saveButton.click()

  await expect(page.getByRole('status')).toHaveText('Sorumlu güncellendi.')
  expect(fixture.lastAssignmentBody).toEqual({ userId: ASSIGNEE_ADMIN_ID })
  expect(fixture.lastAssignmentCsrf).toBe('browser-test-csrf')
  await expect(assigneeSelect).toHaveValue(ASSIGNEE_ADMIN_ID)

  await assigneeSelect.selectOption('')
  await saveButton.click()

  await expect(page.getByRole('status')).toHaveText('Sorumlu güncellendi.')
  expect(fixture.lastAssignmentBody).toEqual({ userId: null })
  expect(fixture.detail.assignedUserId).toBeNull()
  await expect(assigneeSelect).toHaveValue('')

  await assigneeSelect.focus()
  await expect(assigneeSelect).toBeFocused()
  await assertFocusVisibleOutline(page)
  await assertNoDocumentOverflow(page)
  await testInfo.attach(`ticket-assignment-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })
  telemetry.assertClean()
})

test('http 200 smtp failure keeps status and shows delivery warning', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ status: 'CustomerReplied' })
  fixture.replyMode = 'smtp-failed'
  await installTicketWorkspaceMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)

  const replyText = 'SMTP hata simülasyonu yanıtı'
  await page.getByLabel('Yanıtınız').fill(replyText)
  await page.getByRole('button', { name: 'Yanıtı gönder' }).click()

  await expect(
    page.locator('.ticket-reply__notice[role="alert"]'),
  ).toHaveText(SMTP_FAILED_NOTICE)
  await expect(page.getByLabel('Yanıtınız')).toHaveValue('')
  await expect(page.getByText(replyText)).toBeVisible()
  await expect(page.getByText('Müşteri Yanıtladı')).toBeVisible()
  await expect(page.getByText('Müşteri Bekleniyor')).toHaveCount(0)
  expect(fixture.lastReplyBody).toEqual({ content: replyText })
  expect(fixture.detail.status).toBe('CustomerReplied')

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('direct authenticated unknown ticket shows not-found notice', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${UNKNOWN_TICKET_ID}`)
  await expect(page.getByRole('alert')).toHaveText(NOT_FOUND_NOTICE)
  await expect(
    page.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
  ).toHaveCount(0)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean({ allowNotFoundConsole: true })
})

test('protected detail 401 redirects to expired-session login notice', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture, { detailStatus: 401 })

  // First /me keeps seeded session while detail 401 fires; later /me is 401
  // so login page does not re-bootstrap authenticated state.
  let meCalls = 0
  await page.route(ME_API, async (route) => {
    meCalls += 1
    if (meCalls === 1) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(SEED_USER),
      })
      return
    }
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Unauthorized' }),
    })
  })

  await page.goto('/login')
  await page.evaluate((user) => {
    sessionStorage.removeItem('vshd.accessToken')
    sessionStorage.setItem('vshd.user', JSON.stringify(user))
  }, SEED_USER)
  meCalls = 0
  await page.goto(`/tickets/${TICKET_ID}`)

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

test('keyboard traversal covers back refresh attachment composer and shell', async ({
  page,
}) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture()
  await installTicketWorkspaceMocks(page, fixture)
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)
  await page.locator('body').focus()

  await page.keyboard.press('Tab')
  await tabUntil(page, () => activeMatches(page, '.skip-link'))
  await expect(page.locator('.skip-link')).toBeFocused()

  await tabUntil(page, () =>
    page
      .getByRole('link', { name: 'Destek taleplerine dön' })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(
    page.getByRole('link', { name: 'Destek taleplerine dön' }),
  ).toBeFocused()
  await assertFocusVisibleOutline(page)

  await tabUntil(page, () =>
    page
      .getByRole('button', { name: 'Yenile' })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(page.getByRole('button', { name: 'Yenile' })).toBeFocused()
  await assertFocusVisibleOutline(page)

  await tabUntil(page, () =>
    page
      .getByRole('button', {
        name: `${ATTACHMENT_FILE_NAME} dosyasını indir`,
      })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(
    page.getByRole('button', {
      name: `${ATTACHMENT_FILE_NAME} dosyasını indir`,
    }),
  ).toBeFocused()
  await assertFocusVisibleOutline(page)

  await tabUntil(page, () => activeMatches(page, 'textarea[name="content"]'))
  await expect(page.getByLabel('Yanıtınız')).toBeFocused()
  await page.keyboard.type('Klavye ile yazılan yanıt')

  await page.keyboard.press('Tab')
  await expect(
    page.getByRole('button', { name: 'Yanıtı gönder' }),
  ).toBeFocused()
  await assertFocusVisibleOutline(page)

  await page.locator('body').focus()
  await tabUntil(page, () =>
    page
      .getByRole('button', { name: 'Çıkış yap' })
      .evaluate((el) => el === document.activeElement),
  )
  await expect(page.getByRole('button', { name: 'Çıkış yap' })).toBeFocused()
  await assertFocusVisibleOutline(page)

  await assertNoDocumentOverflow(page)
  telemetry.assertClean()
})

test('reduced motion and long fixtures keep zero document overflow', async ({
  page,
}, testInfo: TestInfo) => {
  const telemetry = attachTelemetry(page)
  const fixture = createFixture({ longContent: true })
  await installTicketWorkspaceMocks(page, fixture)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await seedAuthenticatedSession(page)

  await page.goto(`/tickets/${TICKET_ID}`)
  await expectReadyDetail(page, fixture)

  await expect(page.getByRole('heading', { level: 1 })).toHaveText(LONG_SUBJECT)
  await expect(page.getByText(LONG_MESSAGE)).toBeVisible()
  await expect(
    page.getByRole('button', { name: `${LONG_FILE_NAME} dosyasını indir` }),
  ).toBeVisible()

  const animation = await page.locator('.ticket-detail').evaluate((el) => {
    const style = getComputedStyle(el)
    return {
      duration: style.animationDuration,
      name: style.animationName,
    }
  })
  // prefers-reduced-motion on .ticket-detail sets animation: none
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
  await testInfo.attach(`ticket-detail-long-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  })
  telemetry.assertClean()
})
