import { expect, test, type Page } from '@playwright/test'
import type {
  CurrentUser,
  Parameter,
  ParameterChangeLog,
  UserListItem,
} from '../src/api/types'

const ORIGIN = 'http://127.0.0.1:4173'
const ME_API = `${ORIGIN}/api/auth/me`
const USERS_API = `${ORIGIN}/api/users`
const PARAMETERS_API = `${ORIGIN}/api/parameters`
// List reads are cursor-paginated (?pageSize=50&search&status&cursor).
const TICKETS_LIST_API_PATTERN = /\/api\/tickets(\?.*)?$/
const EMPTY_TICKETS_PAGE = JSON.stringify({
  items: [],
  nextCursor: null,
  hasMore: false,
  counts: {
    all: 0,
    new: 0,
    waitingCustomerReply: 0,
    customerReplied: 0,
    resolved: 0,
  },
})

const admin = {
  userId: '11111111-1111-1111-1111-111111111111',
  fullName: 'Ada Yönetici',
  username: 'ada.admin',
  role: 'Admin',
} satisfies CurrentUser

const support = {
  ...admin,
  username: 'ada.support',
  role: 'Support',
} satisfies CurrentUser

const initialUsers: UserListItem[] = [
  {
    id: admin.userId,
    fullName: admin.fullName,
    username: admin.username,
    email: 'ada.admin@example.test',
    role: 'Admin',
    isActive: true,
    createdAt: '2026-07-21T10:00:00.000Z',
    lastLoginAt: '2026-07-21T12:00:00.000Z',
  },
]

function watchBrowser(page: Page) {
  const errors: string[] = []

  page.on('console', (message) => {
    if (message.type() === 'error') {
      errors.push(message.text())
    }
  })
  page.on('pageerror', (error) => errors.push(error.message))
  page.on('requestfailed', (request) => {
    const failure = request.failure()?.errorText ?? ''
    if (!failure.includes('ERR_ABORTED')) {
      errors.push(`${request.method()} ${request.url()} ${failure}`)
    }
  })

  return () => expect(errors, errors.join('\n')).toEqual([])
}

async function expectNoDocumentOverflow(page: Page) {
  const width = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
    offenders: Array.from(document.querySelectorAll<HTMLElement>('body *'))
      .map((element) => {
        const rect = element.getBoundingClientRect()
        return {
          element: `${element.tagName.toLowerCase()}.${element.className}`,
          left: Math.round(rect.left),
          right: Math.round(rect.right),
          width: Math.round(rect.width),
        }
      })
      .filter(({ left, right }) => left < 0 || right > document.documentElement.clientWidth)
      .slice(0, 12),
    boxes: [
      '.app-shell',
      '.app-header',
      '.app-main',
      '.users-workspace',
      '.users-table-view',
      '.users-table',
      '.parameters-workspace',
      '.parameters-table-view',
    ].flatMap((selector) => {
      const element = document.querySelector<HTMLElement>(selector)
      if (!element) return []
      const style = getComputedStyle(element)
      const rect = element.getBoundingClientRect()
      return [{
        selector,
        rectWidth: Math.round(rect.width),
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        overflowX: style.overflowX,
        minWidth: style.minWidth,
        gridTemplateColumns: style.gridTemplateColumns,
      }]
    }),
  }))
  expect(
    width.scroll,
    JSON.stringify({ offenders: width.offenders, boxes: width.boxes }, null, 2),
  ).toBeLessThanOrEqual(width.client)
}

test('direct Admin bootstrap manages users and parameters responsively', async ({
  page,
  context,
}) => {
  const assertBrowserClean = watchBrowser(page)
  const users: UserListItem[] = [...initialUsers]
  let releaseMe!: () => void
  const meGate = new Promise<void>((resolve) => {
    releaseMe = resolve
  })
  const mutationBodies: unknown[] = []
  const csrfHeaders: Array<string | null> = []
  let parameter: Parameter = {
    key: 'AutoResolve.InactiveDays',
    value: '3',
    description: 'Müşteri bekleme durumundaki otomatik çözüm eşiği (gün)',
    updatedAt: '2026-07-21T12:00:00.000Z',
  }
  let audit: ParameterChangeLog[] = [
    {
      id: 'audit-1',
      parameterKey: parameter.key,
      oldValue: '2',
      newValue: '3',
      changedByUserId: admin.userId,
      changedByUsername: admin.username,
      changedAt: '2026-07-21T12:00:00.000Z',
    },
  ]

  await context.addCookies([
    { name: 'vshd.csrf', value: 'admin-e2e-csrf', url: ORIGIN },
  ])
  await page.addInitScript(() => sessionStorage.clear())
  await page.route(ME_API, async (route) => {
    await meGate
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(admin),
    })
  })
  await page.route(USERS_API, async (route) => {
    const request = route.request()
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(users),
      })
      return
    }

    const body = request.postDataJSON()
    mutationBodies.push(body)
    csrfHeaders.push(await request.headerValue('x-csrf-token'))
    const created: UserListItem = {
      id: '22222222-2222-2222-2222-222222222222',
      fullName: body.fullName,
      username: body.username,
      email: body.email,
      role: body.role,
      isActive: true,
      createdAt: '2026-07-21T13:00:00.000Z',
      lastLoginAt: null,
    }
    users.push(created)
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(created),
    })
  })
  await page.route(`${PARAMETERS_API}/audit?*`, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(audit),
    })
  })
  await page.route(PARAMETERS_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([parameter]),
    })
  })
  await page.route(
    `${PARAMETERS_API}/AutoResolve.InactiveDays`,
    async (route) => {
      const request = route.request()
      const body = request.postDataJSON()
      mutationBodies.push(body)
      csrfHeaders.push(await request.headerValue('x-csrf-token'))
      parameter = {
        ...parameter,
        value: body.value,
        updatedAt: '2026-07-21T13:00:00.000Z',
      }
      audit = [
        {
          ...audit[0]!,
          id: 'audit-2',
          oldValue: '3',
          newValue: body.value,
          changedAt: parameter.updatedAt,
        },
      ]
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(parameter),
      })
    },
  )

  await page.goto('/users')
  await expect(page.getByRole('status')).toContainText(
    'Oturum doğrulanıyor…',
  )
  await expect(page).toHaveURL(/\/users$/)
  releaseMe()

  await expect(
    page.getByRole('heading', { name: 'Kullanıcılar' }),
  ).toBeVisible()
  await expect(
    page.getByRole('table', { name: 'Portal kullanıcıları' }),
  ).toBeVisible()
  await expectNoDocumentOverflow(page)
  const usersTableHandlesOverflow = await page.locator('.users-table-view').evaluate(
    (element) => {
      if (element.scrollWidth <= element.clientWidth) {
        return true
      }
      element.scrollLeft = element.scrollWidth
      return element.scrollLeft > 0
    },
  )
  expect(usersTableHandlesOverflow).toBe(true)

  await page.getByRole('button', { name: 'Kullanıcı ekle' }).click()
  const dialog = page.getByRole('dialog', { name: 'Kullanıcı ekle' })
  await dialog.getByLabel('Ad soyad').fill('Yeni Destek')
  await dialog.getByLabel('Kullanıcı adı').fill('yeni.destek')
  await dialog.getByLabel('E-posta').fill('yeni.destek@example.test')
  await dialog.getByLabel('Parola').fill('Password12345!')
  await dialog.getByRole('button', { name: 'Kaydet' }).click()
  await expect(page.getByText('Kullanıcı eklendi.')).toBeVisible()
  await expect(page.getByText('Yeni Destek')).toBeVisible()

  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  if (viewport!.width <= 767.84) {
    await page.getByRole('button', { name: 'Menüyü aç' }).click()
    await expect(
      page.getByRole('navigation', { name: 'Ana menü' }),
    ).toBeVisible()
  }
  await page.getByRole('link', { name: 'Parametreler' }).click()
  await expect(
    page.getByRole('heading', { name: 'Parametreler' }),
  ).toBeVisible()
  await expectNoDocumentOverflow(page)
  const input = page.getByLabel('AutoResolve.InactiveDays değeri')
  await input.fill('7')
  await page.getByRole('button', { name: 'Kaydet' }).click()
  await expect(page.getByText('Parametre kaydedildi.')).toBeVisible()
  await expect(
    page.getByRole('table', { name: 'Parametre değişiklik geçmişi' }),
  ).toContainText('7')
  await expectNoDocumentOverflow(page)

  expect(mutationBodies).toEqual([
    {
      fullName: 'Yeni Destek',
      username: 'yeni.destek',
      email: 'yeni.destek@example.test',
      password: 'Password12345!',
      role: 'Support',
    },
    { value: '7' },
  ])
  expect(csrfHeaders).toEqual(['admin-e2e-csrf', 'admin-e2e-csrf'])
  assertBrowserClean()
})

test('Support cookie bootstrap cannot open Admin routes', async ({ page }) => {
  const assertBrowserClean = watchBrowser(page)
  await page.addInitScript(() => sessionStorage.clear())
  await page.route(ME_API, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(support),
    })
  })
  await page.route(TICKETS_LIST_API_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: EMPTY_TICKETS_PAGE,
    })
  })

  await page.goto('/parameters')

  await expect(page).toHaveURL(/\/tickets$/)
  await expect(
    page.getByRole('heading', { name: 'Destek talepleri' }),
  ).toBeVisible()
  await expect(
    page.getByRole('link', { name: 'Kullanıcılar' }),
  ).toHaveCount(0)
  await expect(
    page.getByRole('link', { name: 'Parametreler' }),
  ).toHaveCount(0)
  await expectNoDocumentOverflow(page)
  assertBrowserClean()
})
