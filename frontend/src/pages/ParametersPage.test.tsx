import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { ApiError } from '../api/client'
import type { Parameter } from '../api/types'
import { setStoredUser } from '../auth/tokenStorage'

const listParameters = vi.hoisted(() => vi.fn())
const updateParameter = vi.hoisted(() => vi.fn())
const listParameterAudit = vi.hoisted(() => vi.fn())

vi.mock('../api/parametersApi', () => ({
  listParameters,
  updateParameter,
  listParameterAudit,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const sampleParameters: Parameter[] = [
  {
    key: 'AutoResolve.InactiveDays',
    value: '3',
    description:
      'WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)',
    updatedAt: '2026-07-21T12:00:00.000Z',
  },
]

const sampleAudit = [
  {
    id: 'log-1',
    parameterKey: 'AutoResolve.InactiveDays',
    oldValue: '3',
    newValue: '5',
    changedByUserId: 'user-1',
    changedByUsername: 'admin',
    changedAt: '2026-07-21T13:00:00.000Z',
  },
]

function seedSession() {
  const user = {
    userId: 'user-1',
    fullName: 'Admin Kullanıcısı',
    username: 'admin',
    role: 'Admin' as const,
  }
  setStoredUser(user)
  // AuthProvider bootstraps GET /api/auth/me via fetch; keep session authenticated.
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/auth/me')) {
        return Promise.resolve(
          new Response(JSON.stringify(user), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        )
      }
      if (url.includes('/api/auth/logout')) {
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(
        new Response(JSON.stringify({ message: 'not found' }), {
          status: 404,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
    }),
  )
}

function renderParametersPage() {
  seedSession()
  window.history.pushState({}, '', '/parameters')
  return render(<App />)
}

describe('ParametersPage', () => {
  beforeEach(() => {
    listParameters.mockReset()
    updateParameter.mockReset()
    listParameterAudit.mockReset()
    listParameterAudit.mockResolvedValue([])
    sessionStorage.clear()
    window.history.replaceState({}, '', '/')
  })

  it('shows Turkish initial loading with a polite status', async () => {
    const pending = deferred<Parameter[]>()
    listParameters.mockReturnValueOnce(pending.promise)

    renderParametersPage()

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Parametreler yükleniyor…',
      )
    })

    pending.resolve(sampleParameters)
    await screen.findByRole('table')
  })

  it('renders AutoResolve.InactiveDays and table columns', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    renderParametersPage()

    const table = await screen.findByRole('table')
    expect(
      within(table).getByRole('columnheader', { name: 'Anahtar' }),
    ).toBeInTheDocument()
    expect(
      within(table).getByRole('columnheader', { name: 'Açıklama' }),
    ).toBeInTheDocument()
    expect(
      within(table).getByRole('columnheader', { name: 'Değer' }),
    ).toBeInTheDocument()
    expect(
      within(table).getByRole('columnheader', { name: 'Güncellendi' }),
    ).toBeInTheDocument()
    expect(
      within(table).getByText('AutoResolve.InactiveDays'),
    ).toBeInTheDocument()
    expect(
      within(table).getByText(
        'WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)',
      ),
    ).toBeInTheDocument()
    expect(
      within(table).getByLabelText('AutoResolve.InactiveDays değeri'),
    ).toHaveValue('3')
    expect(table.querySelector('time')).toHaveAttribute(
      'dateTime',
      '2026-07-21T12:00:00.000Z',
    )
    expect(
      screen.getByRole('heading', { name: 'Parametreler' }),
    ).toBeInTheDocument()
  })

  it('saves a value with PUT body { value } and shows success notice', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    const updated: Parameter = {
      ...sampleParameters[0]!,
      value: '7',
      updatedAt: '2026-07-21T13:00:00.000Z',
    }
    updateParameter.mockResolvedValueOnce(updated)

    renderParametersPage()
    await screen.findByRole('table')
    const user = userEvent.setup()

    const input = screen.getByLabelText('AutoResolve.InactiveDays değeri')
    await user.clear(input)
    await user.type(input, '7')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => {
      expect(updateParameter).toHaveBeenCalledWith(
        'AutoResolve.InactiveDays',
        '7',
        expect.objectContaining({ signal: expect.any(AbortSignal) }),
      )
    })

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Parametre kaydedildi.',
    )
    expect(input).toHaveValue('7')
    expect(
      screen.getByRole('table', { name: 'Uygulama parametreleri' }).querySelector('time'),
    ).toHaveAttribute('dateTime', '2026-07-21T13:00:00.000Z')
  })

  it('shows Turkish-friendly validation error from ApiError 400', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    updateParameter.mockRejectedValueOnce(
      new ApiError(400, 'A domain rule was violated.', {
        status: 400,
        title: 'A domain rule was violated.',
      }),
    )

    renderParametersPage()
    await screen.findByRole('table')
    const user = userEvent.setup()

    const input = screen.getByLabelText('AutoResolve.InactiveDays değeri')
    await user.clear(input)
    await user.type(input, '0')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Değer geçersiz. Lütfen kontrol edip yeniden deneyin.',
    )
    expect(screen.queryByText('A domain rule was violated.')).not.toBeInTheDocument()
  })

  it('distinguishes network and server load errors', async () => {
    listParameters.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    renderParametersPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
    )

    listParameters.mockRejectedValueOnce(new Error('Server boom'))
    await userEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Parametreler yüklenemedi. Lütfen yeniden deneyin.',
    )
  })

  it('exposes Parametreler nav link when authenticated', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    renderParametersPage()
    await screen.findByRole('table')

    const navLink = screen.getByRole('link', { name: 'Parametreler' })
    expect(navLink).toHaveAttribute('href', '/parameters')
  })

  it('contains no implementation or sprint jargon', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    renderParametersPage()
    await screen.findByRole('table')

    // Allow product keys such as AutoResolve.InactiveDays; ban sprint/impl jargon.
    expect(document.body).not.toHaveTextContent(
      /UC-|JWT|sessionStorage|REST|sprint/i,
    )
  })

  it('renders Son değişiklikler audit history', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    listParameterAudit.mockResolvedValueOnce(sampleAudit)

    renderParametersPage()
    await screen.findByRole('table', { name: 'Uygulama parametreleri' })

    expect(
      screen.getByText('Son değişiklikler'),
    ).toBeInTheDocument()

    const auditTable = await screen.findByRole('table', {
      name: 'Parametre değişiklik geçmişi',
    })
    expect(
      within(auditTable).getByRole('columnheader', { name: 'Zaman' }),
    ).toBeInTheDocument()
    expect(
      within(auditTable).getByRole('columnheader', { name: 'Eski' }),
    ).toBeInTheDocument()
    expect(
      within(auditTable).getByRole('columnheader', { name: 'Yeni' }),
    ).toBeInTheDocument()
    expect(
      within(auditTable).getByRole('columnheader', { name: 'Kullanıcı' }),
    ).toBeInTheDocument()
    expect(within(auditTable).getByText('admin')).toBeInTheDocument()
    expect(within(auditTable).getByText('3')).toBeInTheDocument()
    expect(within(auditTable).getByText('5')).toBeInTheDocument()
    expect(listParameterAudit).toHaveBeenCalledWith(
      expect.objectContaining({ take: 20 }),
    )
  })
})
