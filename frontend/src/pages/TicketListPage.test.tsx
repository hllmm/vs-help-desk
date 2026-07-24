import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import type {
  TicketListItem,
  TicketListPage,
  TicketStatusCounts,
} from '../api/types'
import { setStoredUser } from '../auth/tokenStorage'

const fetchTickets = vi.hoisted(() => vi.fn())

vi.mock('../api/ticketsApi', () => ({ fetchTickets }))

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
    lastActivityAt: '2026-08-02T08:20:00.000Z',
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
  }),
  ticket({
    id: '2',
    ticketNumber: 'VS-000100',
    subject: 'Fatura sorunu',
    customerName: 'Ali Demir',
    status: 'WaitingCustomerReply',
  }),
]

const counts: TicketStatusCounts = {
  all: 87,
  new: 21,
  waitingCustomerReply: 33,
  customerReplied: 18,
  resolved: 15,
}

function page(
  items: TicketListItem[],
  options: Partial<Omit<TicketListPage, 'items'>> = {},
): TicketListPage {
  return {
    items,
    nextCursor: null,
    hasMore: false,
    counts,
    ...options,
  }
}

function seedSession() {
  const user = {
    userId: 'user-1',
    fullName: 'Destek Kullanıcısı',
    username: 'support',
    role: 'Support' as const,
  }
  setStoredUser(user)
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

function renderTicketsPage(width = 1280) {
  Object.defineProperty(window, 'innerWidth', {
    configurable: true,
    value: width,
  })
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

  it('shows Turkish initial loading and empty guidance', async () => {
    const pending = deferred<TicketListPage>()
    fetchTickets.mockReturnValueOnce(pending.promise)
    renderTicketsPage()

    expect(
      await screen.findByText('Destek talepleri yükleniyor…'),
    ).toHaveAttribute('role', 'status')

    pending.resolve(page([], { counts: { ...counts, all: 0 } }))
    expect(await screen.findByText('Henüz destek talebi yok.')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Yeni e-postalar geldiğinde destek talepleri burada görünür.',
      ),
    ).toBeInTheDocument()
  })

  it('uses authoritative server counts in the lifecycle rail and result count', async () => {
    fetchTickets.mockResolvedValueOnce(page(sampleTickets))
    renderTicketsPage()

    const rail = await screen.findByRole('group', {
      name: 'Destek talebi durumları',
    })
    expect(within(rail).getByRole('button', { name: /Tümü/ })).toHaveTextContent(
      '87',
    )
    expect(within(rail).getByRole('button', { name: /Yeni/ })).toHaveTextContent(
      '21',
    )
    expect(
      within(rail).getByRole('button', { name: /Müşteri Bekleniyor/ }),
    ).toHaveTextContent('33')
    expect(screen.getByText('87 sonuç')).toBeInTheDocument()
  })

  it('holds over-long search locally with a hint instead of hitting the server', async () => {
    fetchTickets.mockResolvedValueOnce(page(sampleTickets))
    renderTicketsPage()
    await screen.findByRole('table')
    const user = userEvent.setup()
    const search = screen.getByLabelText('Taleplerde ara')

    await user.type(search, 'x'.repeat(101))
    expect(
      screen.getByText('Aramak için en fazla 100 karakter girin.'),
    ).toBeInTheDocument()
    await new Promise((resolve) => window.setTimeout(resolve, 350))
    expect(fetchTickets).toHaveBeenCalledTimes(1)

    await user.clear(search)
    await user.type(search, 'y'.repeat(100))
    await waitFor(
      () => {
        expect(fetchTickets).toHaveBeenCalledTimes(2)
      },
      { timeout: 800 },
    )
    expect(fetchTickets.mock.calls[1]?.[0]).toEqual(
      expect.objectContaining({ search: 'y'.repeat(100), pageSize: 50 }),
    )
    expect(
      screen.queryByText('Aramak için en fazla 100 karakter girin.'),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByText('Aramak için en az 2 karakter girin.'),
    ).not.toBeInTheDocument()
  })

  it('keeps controls mounted and search focused through server replacements', async () => {
    const searchResult = deferred<TicketListPage>()
    const statusResult = deferred<TicketListPage>()
    fetchTickets
      .mockResolvedValueOnce(page(sampleTickets))
      .mockReturnValueOnce(searchResult.promise)
      .mockReturnValueOnce(statusResult.promise)
    renderTicketsPage()
    await screen.findByRole('table')
    const user = userEvent.setup()
    const search = screen.getByLabelText('Taleplerde ara')

    await user.type(search, 'İ')
    expect(
      screen.getByText('Aramak için en az 2 karakter girin.'),
    ).toBeInTheDocument()
    await new Promise((resolve) => window.setTimeout(resolve, 350))
    expect(fetchTickets).toHaveBeenCalledTimes(1)

    await user.type(search, 'ş')
    await waitFor(
      () => {
        expect(fetchTickets).toHaveBeenCalledTimes(2)
      },
      { timeout: 800 },
    )
    expect(fetchTickets.mock.calls[1]?.[0]).toEqual(
      expect.objectContaining({ search: 'İş', pageSize: 50 }),
    )
    expect(screen.getByLabelText('Taleplerde ara')).toBe(search)
    expect(search).toHaveFocus()
    expect(
      screen.queryByText('Aramak için en az 2 karakter girin.'),
    ).not.toBeInTheDocument()

    searchResult.resolve(
      page([sampleTickets[0]!], { counts: { ...counts, all: 1 } }),
    )
    await screen.findByText('VS-000042')

    await user.selectOptions(screen.getByLabelText('Durum'), 'Resolved')
    await waitFor(() => expect(fetchTickets).toHaveBeenCalledTimes(3))
    expect(screen.getByLabelText('Taleplerde ara')).toBeInTheDocument()
    expect(screen.getByLabelText('Durum')).toHaveValue('Resolved')

    statusResult.resolve(page([], { counts: { ...counts, resolved: 0 } }))
    expect(
      await screen.findByText('Aramanızla eşleşen destek talebi bulunamadı.'),
    ).toBeInTheDocument()
  })

  it('treats refresh after a successful empty page as post-initialization', async () => {
    const refresh = deferred<TicketListPage>()
    fetchTickets
      .mockResolvedValueOnce(page([], { counts: { ...counts, all: 0 } }))
      .mockReturnValueOnce(refresh.promise)
  })

  it('explains loaded counts and clears search and status together', async () => {
    fetchTickets.mockResolvedValueOnce(page(sampleTickets))
    renderTicketsPage()
    const user = userEvent.setup()

    expect(
      await screen.findByRole('heading', {
        name: 'Yüklenen taleplerin durum dağılımı',
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Sayılar şu anda yüklenen talepleri gösterir.'),
    ).toBeInTheDocument()

    await user.type(screen.getByLabelText('Taleplerde ara'), 'eşleşmez')
    await user.selectOptions(screen.getByLabelText('Durum'), 'Resolved')
    await user.click(
      screen.getByRole('button', { name: 'Filtreleri temizle' }),
    )

    expect(screen.getByLabelText('Taleplerde ara')).toHaveValue('')
    expect(screen.getByLabelText('Durum')).toHaveValue('all')
    expect(
      screen.queryByRole('button', { name: 'Filtreleri temizle' }),
    ).not.toBeInTheDocument()
    expect(screen.getByRole('table')).toBeInTheDocument()
  })
    renderTicketsPage()
    await screen.findByText('Henüz destek talebi yok.')

    await userEvent.click(screen.getByRole('button', { name: 'Yenile' }))

    expect(screen.getByLabelText('Taleplerde ara')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Yenileniyor…' })).toBeDisabled()
    expect(
      screen.queryByText('Destek talepleri yükleniyor…'),
    ).not.toBeInTheDocument()

    refresh.reject(new Error('Server boom'))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Destek talepleri güncellenemedi.')
    expect(within(alert).getByRole('button', { name: 'Yeniden dene' })).toBeEnabled()
    expect(screen.getByLabelText('Taleplerde ara')).toBeInTheDocument()
    expect(alert).not.toHaveTextContent(
      'Destek talepleri yüklenemedi. Lütfen yeniden deneyin.',
    )
  })

  it('loads more only when available and disables the action while appending', async () => {
    const append = deferred<TicketListPage>()
    fetchTickets
      .mockResolvedValueOnce(
        page([sampleTickets[0]!], { nextCursor: 'cursor-2', hasMore: true }),
      )
      .mockReturnValueOnce(append.promise)
    renderTicketsPage()

    const loadMore = await screen.findByRole('button', { name: 'Daha fazla yükle' })
    await userEvent.click(loadMore)

    expect(
      screen.getByRole('button', { name: 'Yükleniyor…' }),
    ).toBeDisabled()
    append.resolve(page([sampleTickets[1]!]))
    await screen.findByText('VS-000100')
    expect(
      screen.queryByRole('button', { name: 'Daha fazla yükle' }),
    ).not.toBeInTheDocument()
  })

  it('resets accumulated rows when status changes and sends the server filter', async () => {
    const filtered = deferred<TicketListPage>()
    fetchTickets
      .mockResolvedValueOnce(
        page(sampleTickets, { nextCursor: 'cursor-2', hasMore: true }),
      )
      .mockResolvedValueOnce(page([ticket({ id: '3' })]))
      .mockReturnValueOnce(filtered.promise)
    renderTicketsPage()
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla yükle' }))
    await screen.findByText('VS-000003')

    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Resolved')
    await waitFor(() => expect(fetchTickets).toHaveBeenCalledTimes(3))
    expect(screen.queryByText('VS-000042')).not.toBeInTheDocument()
    expect(fetchTickets.mock.calls[2]?.[0]).toEqual(
      expect.objectContaining({ status: 'Resolved', pageSize: 50 }),
    )

    filtered.resolve(
      page([
        ticket({ id: '4', ticketNumber: 'VS-000400', status: 'Resolved' }),
      ]),
    )
    expect(await screen.findByText('VS-000400')).toBeInTheDocument()
  })

  it.each([1280, 320])(
    'renders each ticket once in one semantic table at %ipx',
    async (width) => {
      fetchTickets.mockResolvedValueOnce(page([sampleTickets[0]!]))
      renderTicketsPage(width)

      const table = await screen.findByRole('table', { name: 'Destek talepleri' })
      expect(screen.getAllByText('VS-000042')).toHaveLength(1)
      expect(screen.queryByRole('list', { name: 'Destek talepleri' })).not.toBeInTheDocument()
      expect(within(table).getByRole('columnheader', { name: 'Numara' })).toBeInTheDocument()
      expect(table.querySelector('td[data-label="Konu"]')).toHaveTextContent(
        'Şifre sıfırlama',
      )
      expect(table.querySelector('td[data-label="Müşteri"]')).toHaveTextContent(
        'İrem Yılmaz',
      )
      expect(table.querySelector('td[data-label="Son hareket"]')).toContainElement(
        table.querySelector('time'),
      )
    },
  )

  it('offers an accessible retry for initial and refresh errors', async () => {
    fetchTickets.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    renderTicketsPage()
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
    )
    const initialRetry = screen.getByRole('button', { name: 'Yeniden dene' })

    fetchTickets.mockResolvedValueOnce(page(sampleTickets))
    await userEvent.click(initialRetry)
    await screen.findByRole('table')

    fetchTickets.mockRejectedValueOnce(new Error('Server boom'))
    await userEvent.click(screen.getByRole('button', { name: 'Yenile' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Destek talepleri güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.',
    )
    expect(within(alert).getByRole('button', { name: 'Yeniden dene' })).toBeEnabled()
    expect(screen.getByText('VS-000042')).toBeInTheDocument()
  })

  it('preserves rows and offers retry after a load-more error', async () => {
    fetchTickets
      .mockResolvedValueOnce(
        page([sampleTickets[0]!], { nextCursor: 'cursor-2', hasMore: true }),
      )
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(page([sampleTickets[1]!]))
    renderTicketsPage()

    await userEvent.click(
      await screen.findByRole('button', { name: 'Daha fazla yükle' }),
    )
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Daha fazla destek talebi yüklenemedi. Mevcut liste korunuyor.',
    )
    expect(screen.getByText('VS-000042')).toBeInTheDocument()

    await userEvent.click(
      within(alert).getByRole('button', { name: 'Yeniden dene' }),
    )
    expect(await screen.findByText('VS-000100')).toBeInTheDocument()
  })

  it('renders unknown statuses with the Turkish fallback', async () => {
    fetchTickets.mockResolvedValueOnce(
      page([ticket({ id: '9', status: 'Escalated' })]),
    )
    renderTicketsPage()

    const badge = await screen.findByText('Bilinmeyen durum')
    expect(badge).toHaveAttribute('data-tone', 'unknown')
  })
})
