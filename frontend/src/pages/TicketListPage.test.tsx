import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import type { TicketListItem } from '../api/types'
import { setStoredUser } from '../auth/tokenStorage'

const fetchTickets = vi.hoisted(() => vi.fn())

vi.mock('../api/ticketsApi', () => ({
  fetchTickets,
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

function ticket(
  overrides: Partial<TicketListItem> & Pick<TicketListItem, 'id'>,
): TicketListItem {
  return {
    ticketNumber: `VS-${overrides.id.padStart(6, '0')}`,
    subject: `Konu ${overrides.id}`,
    customerName: `Müşteri ${overrides.id}`,
    customerEmail: `musteri${overrides.id}@example.com`,
    status: 'New',
    lastActivityAt: '2026-07-20T10:00:00.000Z',
    assignedUserId: null,
    ...overrides,
  }
}

const sampleTickets: TicketListItem[] = [
  ticket({
    id: '1',
    ticketNumber: 'VS-000042',
    subject: 'Şifre sıfırlama',
    customerName: 'İrem Yılmaz',
    customerEmail: 'irem.yilmaz@example.com',
    status: 'New',
  }),
  ticket({
    id: '2',
    ticketNumber: 'VS-000100',
    subject: 'Fatura sorunu',
    customerName: 'Ali Demir',
    customerEmail: 'ali@example.com',
    status: 'WaitingCustomerReply',
  }),
  ticket({
    id: '3',
    ticketNumber: 'VS-000200',
    subject: 'Lisans talebi',
    customerName: 'Ayşe Kaya',
    customerEmail: 'ayse@example.com',
    status: 'CustomerReplied',
  }),
  ticket({
    id: '4',
    ticketNumber: 'VS-000300',
    subject: 'Kurulum yardımı',
    customerName: 'Mehmet Can',
    customerEmail: 'mehmet@example.com',
    status: 'Resolved',
  }),
]

function seedSession() {
  const user = {
    userId: 'user-1',
    fullName: 'Destek Kullanıcısı',
    username: 'support',
    role: 'Support' as const,
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

function renderTicketsPage() {
  seedSession()
  window.history.pushState({}, '', '/tickets')
  return render(<App />)
}

describe('TicketListPage', () => {
  beforeEach(() => {
    fetchTickets.mockReset()
    sessionStorage.clear()
    window.history.replaceState({}, '', '/')
  })

  it('shows Turkish initial loading with a polite status', async () => {
    const pending = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(pending.promise)

    renderTicketsPage()

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Destek talepleri yükleniyor…',
      )
    })

    pending.resolve(sampleTickets)
    await screen.findByRole('table')
  })

  it('renders table and card semantics for ready tickets', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()

    const table = await screen.findByRole('table')
    expect(within(table).getByText('VS-000042')).toBeInTheDocument()
    expect(within(table).getByText('Şifre sıfırlama')).toBeInTheDocument()
    expect(within(table).getByText('İrem Yılmaz')).toBeInTheDocument()
    expect(within(table).getByText('irem.yilmaz@example.com')).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: 'Numara' })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: 'Konu' })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: 'Müşteri' })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: 'Durum' })).toBeInTheDocument()
    expect(
      within(table).getByRole('columnheader', { name: 'Son hareket' }),
    ).toBeInTheDocument()
    expect(within(table).getByText('Yeni')).toBeInTheDocument()
    expect(table.querySelector('time')).toHaveAttribute(
      'dateTime',
      '2026-07-20T10:00:00.000Z',
    )

    const list = screen.getByRole('list', { name: 'Destek talepleri' })
    expect(within(list).getByText('VS-000042')).toBeInTheDocument()
    expect(within(list).getByRole('heading', { name: 'Şifre sıfırlama' })).toBeInTheDocument()
    expect(within(list).getByText('İrem Yılmaz')).toBeInTheDocument()
    expect(within(list).getByText('irem.yilmaz@example.com')).toBeInTheDocument()
    expect(within(list).getByText('Yeni')).toBeInTheDocument()
  })

  it('shows true-empty guidance after a completed empty load', async () => {
    fetchTickets.mockResolvedValueOnce([])
    renderTicketsPage()

    expect(
      await screen.findByText('Henüz destek talebi yok.'),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Yeni e-postalar geldiğinde destek talepleri burada görünür.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows filter-empty guidance without hiding filters', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()

    await screen.findByRole('table')
    const user = userEvent.setup()

    await user.type(
      screen.getByLabelText('Taleplerde ara'),
      'eşleşmeyen-arama-xyz',
    )

    expect(
      await screen.findByText('Aramanızla eşleşen destek talebi bulunamadı.'),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Arama metnini veya durum filtresini değiştirin.'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Taleplerde ara')).toBeInTheDocument()
    expect(screen.getByLabelText('Durum')).toBeInTheDocument()
    expect(
      screen.getByRole('group', { name: 'Destek talebi durumları' }),
    ).toBeInTheDocument()
  })

  it('distinguishes network and server errors', async () => {
    fetchTickets.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    renderTicketsPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
    )

    fetchTickets.mockRejectedValueOnce(new Error('Server boom'))
    await userEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek talepleri yüklenemedi. Lütfen yeniden deneyin.',
    )
  })

  it('retries and disables refresh while busy', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')

    const pending = deferred<TicketListItem[]>()
    fetchTickets.mockReturnValueOnce(pending.promise)
    const user = userEvent.setup()

    const refresh = screen.getByRole('button', { name: 'Yenile' })
    await user.click(refresh)

    expect(screen.getByRole('button', { name: 'Yenileniyor…' })).toBeDisabled()
    const section = screen.getByRole('region', { name: 'Destek talepleri' })
    expect(section).toHaveAttribute('aria-busy', 'true')

    pending.resolve(sampleTickets)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Yenile' })).toBeEnabled()
    })
  })

  it('keeps rows visible with refresh error guidance', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')

    fetchTickets.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Yenile' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek hizmetine ulaşılamadı. Mevcut listeyi görüntülemeye devam edebilir ve yeniden deneyebilirsiniz.',
    )
    expect(within(screen.getByRole('table')).getByText('VS-000042')).toBeInTheDocument()

    fetchTickets.mockRejectedValueOnce(new Error('Server boom'))
    await user.click(screen.getByRole('button', { name: 'Yenile' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek talepleri güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.',
    )
    expect(within(screen.getByRole('table')).getByText('VS-000042')).toBeInTheDocument()
  })

  it('searches number subject customer name and email', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')
    const user = userEvent.setup()
    const search = screen.getByLabelText('Taleplerde ara')

    await user.clear(search)
    await user.type(search, '000042')
    expect(within(screen.getByRole('table')).getByText('VS-000042')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).queryByText('VS-000100')).not.toBeInTheDocument()

    await user.clear(search)
    await user.type(search, 'Fatura')
    expect(within(screen.getByRole('table')).getByText('Fatura sorunu')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).queryByText('Şifre sıfırlama')).not.toBeInTheDocument()

    await user.clear(search)
    await user.type(search, 'İrem')
    expect(within(screen.getByRole('table')).getByText('İrem Yılmaz')).toBeInTheDocument()

    await user.clear(search)
    await user.type(search, 'ali@example.com')
    expect(within(screen.getByRole('table')).getByText('ali@example.com')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).queryByText('VS-000042')).not.toBeInTheDocument()
  })

  it('keeps select and lifecycle rail synchronized', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')
    const user = userEvent.setup()

    const select = screen.getByLabelText('Durum')
    const rail = screen.getByRole('group', { name: 'Destek talebi durumları' })

    await user.selectOptions(select, 'WaitingCustomerReply')
    expect(select).toHaveValue('WaitingCustomerReply')
    expect(
      within(rail).getByRole('button', { name: /Müşteri Bekleniyor/ }),
    ).toHaveAttribute('aria-pressed', 'true')
    expect(within(screen.getByRole('table')).getByText('VS-000100')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).queryByText('VS-000042')).not.toBeInTheDocument()

    await user.click(within(rail).getByRole('button', { name: /Çözüldü/ }))
    expect(select).toHaveValue('Resolved')
    expect(
      within(rail).getByRole('button', { name: /Çözüldü/ }),
    ).toHaveAttribute('aria-pressed', 'true')
    expect(within(screen.getByRole('table')).getByText('VS-000300')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).queryByText('VS-000100')).not.toBeInTheDocument()

    await user.click(within(rail).getByRole('button', { name: /Tümü/ }))
    expect(select).toHaveValue('all')
  })

  it('calculates rail counts after search but before status filter', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Taleplerde ara'), 'VS-000')
    const rail = screen.getByRole('group', { name: 'Destek talebi durumları' })

    // All four tickets match VS-000 prefix
    expect(within(rail).getByRole('button', { name: /Tümü/ })).toHaveTextContent('4')
    expect(within(rail).getByRole('button', { name: /Yeni/ })).toHaveTextContent('1')
    expect(
      within(rail).getByRole('button', { name: /Müşteri Bekleniyor/ }),
    ).toHaveTextContent('1')

    await user.selectOptions(screen.getByLabelText('Durum'), 'New')
    // Counts remain based on search, not status filter
    expect(within(rail).getByRole('button', { name: /Tümü/ })).toHaveTextContent('4')
    expect(within(rail).getByRole('button', { name: /Yeni/ })).toHaveTextContent('1')
    expect(
      within(rail).getByRole('button', { name: /Müşteri Bekleniyor/ }),
    ).toHaveTextContent('1')
  })

  it('shows the final filtered result count', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')

    expect(screen.getByText('4 sonuç')).toBeInTheDocument()

    const user = userEvent.setup()
    await user.selectOptions(screen.getByLabelText('Durum'), 'Resolved')
    expect(screen.getByText('1 sonuç')).toBeInTheDocument()
  })

  it('renders an unknown status as Turkish fallback with unknown tone', async () => {
    fetchTickets.mockResolvedValueOnce([
      ticket({
        id: '9',
        ticketNumber: 'VS-000999',
        subject: 'Özel durum',
        status: 'Escalated',
      }),
    ])
    renderTicketsPage()

    const table = await screen.findByRole('table')
    expect(within(table).getByText('Bilinmeyen durum')).toBeInTheDocument()
    const badge = within(table).getByText('Bilinmeyen durum')
    expect(badge).toHaveAttribute('data-tone', 'unknown')
  })

  it('contains no implementation or sprint jargon', async () => {
    fetchTickets.mockResolvedValueOnce(sampleTickets)
    renderTicketsPage()
    await screen.findByRole('table')

    expect(document.body).not.toHaveTextContent(
      /UC-|JWT|sessionStorage|REST|Day|sprint/i,
    )
  })
})
