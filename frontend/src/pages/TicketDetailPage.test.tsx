import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import type {
  ResolveTicketResult,
  SupportReplyResult,
  TicketDetails,
  TicketListItem,
  TicketListPage,
} from '../api/types'
import { setStoredUser } from '../auth/tokenStorage'
import { RESOLUTION_COPY } from '../features/ticket-details/useResolveTicket'
import { REPLY_OUTCOME_MESSAGES } from '../features/ticket-details/useTicketReply'

const fetchTicketDetails = vi.hoisted(() => vi.fn())
const fetchTicketMessages = vi.hoisted(() => vi.fn())
const fetchTickets = vi.hoisted(() => vi.fn())
const replyToTicket = vi.hoisted(() => vi.fn())
const resolveTicket = vi.hoisted(() => vi.fn())
const fetchAssignableUsers = vi.hoisted(() => vi.fn())
const assignTicket = vi.hoisted(() => vi.fn())
const downloadAttachment = vi.hoisted(() => vi.fn())

vi.mock('../api/ticketsApi', () => ({
  fetchTicketDetails,
  fetchTicketMessages,
  fetchTickets,
  replyToTicket,
  resolveTicket,
  fetchAssignableUsers,
  assignTicket,
}))

vi.mock('../api/attachmentsApi', () => ({
  downloadAttachment,
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

function sampleDetail(overrides: Partial<TicketDetails> = {}): TicketDetails {
  return {
    id: 'ticket-1',
    ticketNumber: 'VS-000042',
    subject: 'Şifre sıfırlama',
    customerName: 'İrem Yılmaz',
    customerEmail: 'irem.yilmaz@example.com',
    status: 'New',
    assignedUserId: null,
    createdAt: '2026-07-20T09:00:00.000Z',
    updatedAt: '2026-07-20T10:00:00.000Z',
    lastActivityAt: '2026-07-20T10:00:00.000Z',
    waitingCustomerSince: null,
    resolvedAt: null,
    closedByUserId: null,
    nextMessageCursor: null,
    hasMoreMessages: false,
    messages: [
      {
        id: 'msg-1',
        senderType: 'Customer',
        userId: null,
        content: 'Merhaba, şifremi unuttum.',
        isHtml: false,
        createdAt: '2026-07-20T09:05:00.000Z',
      },
      {
        id: 'msg-2',
        senderType: 'Support',
        userId: 'user-1',
        content: 'Merhaba, yardımcı oluyoruz.',
        isHtml: false,
        createdAt: '2026-07-20T09:30:00.000Z',
      },
    ],
    attachments: [
      {
        id: 'att-1',
        ticketMessageId: 'msg-1',
        fileName: 'ekran.png',
        contentType: 'image/png',
        fileSize: 2048,
        createdAt: '2026-07-20T09:05:01.000Z',
      },
    ],
    ...overrides,
  }
}

function listTicket(
  overrides: Partial<TicketListItem> & Pick<TicketListItem, 'id'> = {
    id: 'ticket-1',
  },
): TicketListItem {
  return {
    ticketNumber: 'VS-000042',
    subject: 'Şifre sıfırlama',
    customerName: 'İrem Yılmaz',
    customerEmail: 'irem.yilmaz@example.com',
    status: 'New',
    lastActivityAt: '2026-07-20T10:00:00.000Z',
    assignedUserId: null,
    ...overrides,
  }
}

function listPage(items: TicketListItem[]): TicketListPage {
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

function renderDetail(ticketId = 'ticket-1') {
  seedSession()
  window.history.pushState({}, '', `/tickets/${ticketId}`)
  return render(<App />)
}

function renderList() {
  seedSession()
  window.history.pushState({}, '', '/tickets')
  return render(<App />)
}

function sampleReply(
  overrides: Partial<SupportReplyResult> = {},
): SupportReplyResult {
  return {
    ticketId: 'ticket-1',
    ticketNumber: 'VS-000042',
    messageId: 'msg-reply-1',
    status: 'WaitingCustomerReply',
    emailDelivered: true,
    ticketStateUpdated: true,
    noticeCode: null,
    ...overrides,
  }
}

function sampleResolve(
  overrides: Partial<ResolveTicketResult> = {},
): ResolveTicketResult {
  return {
    ticketId: 'ticket-1',
    ticketNumber: 'VS-000042',
    status: 'Resolved',
    resolvedAt: '2026-07-20T12:00:00.000Z',
    updatedAt: '2026-07-20T12:00:00.000Z',
    lastActivityAt: '2026-07-20T12:00:00.000Z',
    closedByUserId: 'user-1',
    changed: true,
    ...overrides,
  }
}

describe('TicketDetailPage', () => {
  beforeEach(() => {
    fetchTicketDetails.mockReset()
    fetchTicketMessages.mockReset()
    fetchTickets.mockReset()
    replyToTicket.mockReset()
    resolveTicket.mockReset()
    fetchAssignableUsers.mockReset()
    assignTicket.mockReset()
    downloadAttachment.mockReset()
    fetchAssignableUsers.mockResolvedValue([
      {
        id: 'user-1',
        fullName: 'Destek Kullanıcısı',
        username: 'support',
      },
    ])
    sessionStorage.clear()
    window.history.replaceState({}, '', '/')
  })

  it('shows a polite initial loader on /tickets/:ticketId', async () => {
    const pending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(pending.promise)

    renderDetail()

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'Talep ayrıntıları yükleniyor…',
      )
    })
    expect(
      screen.getByRole('link', { name: 'Destek taleplerine dön' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Çıkış yap' })).toBeEnabled()

    pending.resolve(sampleDetail())
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })
  })

  it('renders ready header, customer metadata, status, and timeline', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()

    expect(
      await screen.findByRole('link', { name: 'Destek taleplerine dön' }),
    ).toHaveAttribute('href', '/tickets')
    expect(await screen.findByText('VS-000042')).toBeInTheDocument()
    expect(screen.getByText('Şifre sıfırlama')).toBeInTheDocument()
    expect(screen.getByText('Yeni')).toBeInTheDocument()
    expect(screen.getByText('Son hareket')).toBeInTheDocument()
    expect(screen.getByText('İrem Yılmaz')).toBeInTheDocument()
    expect(screen.getByText('irem.yilmaz@example.com')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Sorumlu' })).toBeInTheDocument()
    expect(await screen.findByLabelText('Atanan destek personeli')).toHaveValue('')
    const customerMeta = document.querySelector('.ticket-detail__customer')
    expect(customerMeta).not.toBeNull()
    expect(within(customerMeta as HTMLElement).getByText('Müşteri')).toBeInTheDocument()
    expect(within(customerMeta as HTMLElement).getByText('E-posta')).toBeInTheDocument()
    expect(
      within(customerMeta as HTMLElement).getByText('Açılış zamanı'),
    ).toBeInTheDocument()
    expect(customerMeta?.querySelector('time')).toHaveAttribute(
      'dateTime',
      '2026-07-20T09:00:00.000Z',
    )
    expect(
      screen.getByRole('heading', { name: 'Mesaj geçmişi' }),
    ).toBeInTheDocument()

    const timeline = screen.getByRole('list', { name: 'Mesaj geçmişi' })
    expect(timeline.tagName).toBe('OL')
    const articles = within(timeline).getAllByRole('article')
    expect(articles).toHaveLength(2)
    expect(within(articles[0]!).getByText('Müşteri')).toBeInTheDocument()
    expect(within(articles[1]!).getByText('Destek ekibi')).toBeInTheDocument()
    expect(within(articles[0]!).getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
    expect(articles[0]!.querySelector('time')).toHaveAttribute(
      'dateTime',
      '2026-07-20T09:05:00.000Z',
    )
  })

  it('distinguishes customer, system, and support messages in server order', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        messages: [
          sampleDetail().messages[0]!,
          {
            id: 'msg-system',
            senderType: 'System',
            userId: null,
            content: 'Talep otomatik olarak güncellendi.',
            isHtml: false,
            createdAt: '2026-07-20T09:20:00.000Z',
          },
          sampleDetail().messages[1]!,
        ],
      }),
    )
    renderDetail()

    const timeline = await screen.findByRole('list', {
      name: 'Mesaj geçmişi',
    })
    const articles = within(timeline).getAllByRole('article')
    expect(articles).toHaveLength(3)
    expect(articles[0]?.closest('li')).toHaveAttribute(
      'data-sender',
      'customer',
    )
    expect(articles[1]?.closest('li')).toHaveAttribute('data-sender', 'system')
    expect(articles[2]?.closest('li')).toHaveAttribute('data-sender', 'support')
    expect(within(articles[1]!).getByText('Sistem')).toBeInTheDocument()
    expect(
      within(articles[1]!).getByText('Talep otomatik olarak güncellendi.'),
    ).toBeInTheDocument()
  })

  it('shows empty timeline guidance when there are no messages', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({ messages: [], attachments: [] }),
    )
    renderDetail()

    expect(
      await screen.findByText('Bu talepte henüz mesaj yok.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'Mesaj geçmişi' })).not.toBeInTheDocument()
  })

  it('shows dedicated 404 with back link', async () => {
    const { ApiError } = await import('../api/client')
    fetchTicketDetails.mockRejectedValueOnce(new ApiError(404, 'missing'))
    renderDetail('missing-id')

    expect(
      await screen.findByRole('alert'),
    ).toHaveTextContent('Destek talebi bulunamadı.')
    expect(
      screen.getByRole('link', { name: 'Destek taleplerine dön' }),
    ).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Mesaj geçmişi' })).not.toBeInTheDocument()
  })

  it('shows initial network error with retry', async () => {
    fetchTicketDetails.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    renderDetail()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
    )

    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    await userEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })
  })

  it('shows initial server error with retry', async () => {
    const { ApiError } = await import('../api/client')
    fetchTicketDetails.mockRejectedValueOnce(new ApiError(500, 'boom'))
    renderDetail()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Talep ayrıntıları yüklenemedi. Lütfen yeniden deneyin.',
    )

    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    await userEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))
    await screen.findByText('VS-000042')
  })

  it('keeps the timeline during refresh and shows a small alert on refresh failure', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    const pending = deferred<TicketDetails>()
    fetchTicketDetails.mockReturnValueOnce(pending.promise)
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: 'Yenile' }))
    expect(screen.getByRole('button', { name: 'Yenileniyor…' })).toBeDisabled()
    expect(
      screen.getByRole('link', { name: 'Destek taleplerine dön' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Çıkış yap' })).toBeEnabled()
    expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()

    fetchTicketDetails.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    pending.reject(new TypeError('Failed to fetch'))

    // refresh rejects; hook may use the rejected promise — ensure error path
    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })
    expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
    expect(screen.getByText('VS-000042')).toBeInTheDocument()
  })

  it('supports direct protected navigation to the detail route', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    seedSession()
    window.history.pushState({}, '', '/tickets/ticket-1')
    render(<App />)

    expect(await screen.findByText('VS-000042')).toBeInTheDocument()
    expect(fetchTicketDetails).toHaveBeenCalledWith('ticket-1', {
      signal: expect.any(AbortSignal),
    })
    expect(screen.getByRole('button', { name: 'Çıkış yap' })).toBeInTheDocument()
  })

  it('renders HTML-looking message content as literal text only', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        messages: [
          {
            id: 'msg-x',
            senderType: 'Customer',
            userId: null,
            content:
              '<img onerror="alert(1)" src=x><strong>kalın</strong>',
            isHtml: true,
            createdAt: '2026-07-20T09:05:00.000Z',
          },
        ],
        attachments: [],
      }),
    )
    renderDetail()

    const article = await screen.findByRole('article')
    expect(article).toHaveTextContent('<img onerror="alert(1)" src=x><strong>kalın</strong>')
    expect(article.querySelector('img')).toBeNull()
    expect(article.querySelector('strong')).toBeNull()
  })

  it('renders attachments only inside their owning message and skips empty Ekler regions', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()

    const timeline = await screen.findByRole('list', { name: 'Mesaj geçmişi' })
    const articles = within(timeline).getAllByRole('article')

    expect(within(articles[0]!).getByText('Ekler')).toBeInTheDocument()
    expect(
      within(articles[0]!).getByRole('button', { name: /ekran\.png/ }),
    ).toBeInTheDocument()
    expect(within(articles[1]!).queryByText('Ekler')).not.toBeInTheDocument()
  })

  it('applies wrapping classes to long subject, body, name, email, and file name', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        subject: 'x'.repeat(120),
        customerName: 'n'.repeat(80),
        customerEmail: `${'a'.repeat(40)}@example.com`,
        messages: [
          {
            id: 'msg-long',
            senderType: 'Customer',
            userId: null,
            content: 'body-'.repeat(40),
            isHtml: false,
            createdAt: '2026-07-20T09:05:00.000Z',
          },
        ],
        attachments: [
          {
            id: 'att-long',
            ticketMessageId: 'msg-long',
            fileName: `${'f'.repeat(60)}.pdf`,
            contentType: 'application/pdf',
            fileSize: 10,
            createdAt: '2026-07-20T09:05:01.000Z',
          },
        ],
      }),
    )
    renderDetail()

    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })
    expect(document.querySelector('.ticket-detail__subject--wrap')).not.toBeNull()
    expect(document.querySelector('.ticket-detail__name--wrap')).not.toBeNull()
    expect(document.querySelector('.ticket-detail__email--wrap')).not.toBeNull()
    expect(document.querySelector('.ticket-timeline__body--wrap')).not.toBeNull()
    expect(document.querySelector('.ticket-timeline__file-name--wrap')).not.toBeNull()
  })

  it('shows distinct download error alerts without raw response text', async () => {
    const { ApiError } = await import('../api/client')
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('button', { name: /ekran\.png/ })

    downloadAttachment.mockRejectedValueOnce(new ApiError(404, 'not found body'))
    await userEvent.click(screen.getByRole('button', { name: /ekran\.png/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ek dosya bulunamadı.')
    expect(screen.getByRole('alert')).not.toHaveTextContent('not found body')

    downloadAttachment.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await userEvent.click(screen.getByRole('button', { name: /ekran\.png/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ek dosya indirilemedi. Bağlantınızı kontrol edip yeniden deneyin.',
    )

    downloadAttachment.mockRejectedValueOnce(new ApiError(503, 'upstream'))
    await userEvent.click(screen.getByRole('button', { name: /ekran\.png/ }))
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ek dosya indirilemedi. Lütfen yeniden deneyin.',
    )
    expect(screen.getByRole('alert')).not.toHaveTextContent('upstream')
  })

  it('links desktop table number and subject to the encoded detail route', async () => {
    fetchTickets.mockResolvedValueOnce(
      listPage([
        listTicket({ id: 'id/with spaces', ticketNumber: 'VS-000042' }),
      ]),
    )
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({ id: 'id/with spaces' }),
    )
    renderList()

    const table = await screen.findByRole('table')
    const numberLink = within(table).getByRole('link', {
      name: 'VS-000042 talebini aç',
    })
    const subjectLink = within(table).getByRole('link', {
      name: 'VS-000042: Şifre sıfırlama',
    })
    const expectedHref = `/tickets/${encodeURIComponent('id/with spaces')}`
    expect(numberLink).toHaveAttribute('href', expectedHref)
    expect(subjectLink).toHaveAttribute('href', expectedHref)
  })

  it('keeps one table representation and ticket-number link on mobile', async () => {
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      value: 320,
    })
    fetchTickets.mockResolvedValueOnce(
      listPage([listTicket({ id: 'ticket-1' })]),
    )
    renderList()

    const table = await screen.findByRole('table', { name: 'Destek talepleri' })
    expect(screen.queryByRole('list', { name: 'Destek talepleri' })).not.toBeInTheDocument()
    expect(screen.getAllByText('VS-000042')).toHaveLength(1)
    expect(
      within(table).getByRole('link', { name: 'VS-000042 talebini aç' }),
    ).toHaveAttribute('href', '/tickets/ticket-1')
  })

  it('contains no implementation jargon on the detail page', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    expect(document.body).not.toHaveTextContent(
      /UC-|BR-|JWT|sessionStorage|REST|Sprint|Day [0-9]/i,
    )
  })

  it('renders the reply composer after the timeline on the ready detail page', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()

    expect(
      await screen.findByRole('heading', { name: 'Müşteriye yanıt ver' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Yanıtı gönder' }),
    ).toBeInTheDocument()
    expect(screen.getByText(/Kalan karakter:/)).toBeInTheDocument()
  })

  it('does not show an optimistic timeline item before refresh resolves a saved reply', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    const replyPending = deferred<SupportReplyResult>()
    const refreshPending = deferred<TicketDetails>()
    replyToTicket.mockReturnValueOnce(replyPending.promise)

    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Yanıtınız'), 'Yeni destek yanıtı')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    const timeline = screen.getByRole('list', { name: 'Mesaj geçmişi' })
    expect(
      within(timeline).queryByText('Yeni destek yanıtı'),
    ).not.toBeInTheDocument()

    const saved = sampleDetail({
      status: 'WaitingCustomerReply',
      messages: [
        ...sampleDetail().messages,
        {
          id: 'msg-reply-1',
          senderType: 'Support',
          userId: 'user-1',
          content: 'Yeni destek yanıtı',
          isHtml: false,
          createdAt: '2026-07-20T11:00:00.000Z',
        },
      ],
    })

    fetchTicketDetails.mockReturnValueOnce(refreshPending.promise)
    replyPending.resolve(sampleReply())

    await waitFor(() => {
      expect(fetchTicketDetails).toHaveBeenCalledTimes(2)
    })
    expect(
      within(timeline).queryByText('Yeni destek yanıtı'),
    ).not.toBeInTheDocument()
    expect(screen.queryByText(REPLY_OUTCOME_MESSAGES.delivered)).not.toBeInTheDocument()

    refreshPending.resolve(saved)

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered),
    ).toBeInTheDocument()
    expect(
      within(screen.getByRole('list', { name: 'Mesaj geçmişi' })).getByText(
        'Yeni destek yanıtı',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Müşteri Bekleniyor')).toBeInTheDocument()
    expect(replyToTicket).toHaveBeenCalledTimes(1)
  })

  it('keeps the delivery notice and shows a separate refresh warning when post-save refresh fails', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    replyToTicket.mockResolvedValueOnce(sampleReply())
    fetchTicketDetails.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Yanıtınız'), 'Kaydedildi')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('')

    await waitFor(() => {
      expect(
        screen.getByText(
          'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
        ),
      ).toBeInTheDocument()
    })
    expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
    expect(replyToTicket).toHaveBeenCalledTimes(1)
  })

  it('refreshes on 409 conflict, preserves the draft, and states that no reply was sent', async () => {
    const { ApiError } = await import('../api/client')
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    replyToTicket.mockRejectedValueOnce(new ApiError(409, 'Conflict'))
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({ status: 'CustomerReplied' }),
    )

    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Yanıtınız'), 'Çakışan taslak')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.preSendConflict),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('Çakışan taslak')
    expect(fetchTicketDetails).toHaveBeenCalledTimes(2)
    expect(replyToTicket).toHaveBeenCalledTimes(1)
  })

  it('refreshes on network-ambiguous reply failure while preserving the draft', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    replyToTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())

    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Yanıtınız'), 'Belirsiz')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      await screen.findByText(REPLY_OUTCOME_MESSAGES.networkAmbiguous),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Yanıtınız')).toHaveValue('Belirsiz')
    expect(fetchTicketDetails).toHaveBeenCalledTimes(2)
    expect(replyToTicket).toHaveBeenCalledTimes(1)
  })

  it('keeps download, back, refresh, and logout available while the reply is submitting', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    renderDetail()
    await screen.findByRole('heading', { name: 'Mesaj geçmişi' })

    const pending = deferred<SupportReplyResult>()
    replyToTicket.mockReturnValueOnce(pending.promise)

    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Yanıtınız'), 'Gönderiliyor')
    await user.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect(
      screen.getByRole('button', { name: 'Yanıt gönderiliyor…' }),
    ).toBeDisabled()
    expect(screen.getByLabelText('Yanıtınız')).toBeDisabled()
    expect(
      screen.getByRole('link', { name: 'Destek taleplerine dön' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Çıkış yap' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Yenile' })).toBeEnabled()
    expect(screen.getByRole('button', { name: /ekran\.png/ })).toBeEnabled()

    pending.resolve(sampleReply())
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
    await screen.findByText(REPLY_OUTCOME_MESSAGES.delivered)
  })

  it('shows resolution panel and reply composer for every open status', async () => {
    for (const status of ['New', 'WaitingCustomerReply', 'CustomerReplied']) {
      fetchTicketDetails.mockReset()
      fetchTicketDetails.mockResolvedValueOnce(sampleDetail({ status }))
      const { unmount } = renderDetail()

      expect(
        await screen.findByRole('button', { name: RESOLUTION_COPY.trigger }),
      ).toBeInTheDocument()
      expect(
        screen.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
      ).toBeInTheDocument()
      expect(screen.getByLabelText('Yanıtınız')).toBeInTheDocument()
      expect(
        screen.queryByText(RESOLUTION_COPY.closureNote),
      ).not.toBeInTheDocument()
      unmount()
    }
  })

  it('shows closure note and hides reply composer for Resolved', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        status: 'Resolved',
        resolvedAt: '2026-07-20T12:00:00.000Z',
        closedByUserId: 'user-1',
      }),
    )
    renderDetail()

    expect(await screen.findByText(RESOLUTION_COPY.closureNote)).toBeInTheDocument()
    expect(screen.getByText('Çözüldü')).toBeInTheDocument()
    expect(screen.getByText('Çözülme zamanı')).toBeInTheDocument()
    expect(
      screen.getByText('Çözülme zamanı').parentElement?.querySelector('time'),
    ).toHaveAttribute('dateTime', '2026-07-20T12:00:00.000Z')
    expect(
      screen.queryByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Müşteriye yanıt ver' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Yanıtınız')).not.toBeInTheDocument()
  })

  it('applies HTTP 200 closure before a failed refresh settles', async () => {
    fetchTicketDetails.mockResolvedValueOnce(sampleDetail({ status: 'New' }))
    renderDetail()
    await screen.findByRole('button', { name: RESOLUTION_COPY.trigger })

    resolveTicket.mockResolvedValueOnce(sampleResolve({ changed: true }))
    fetchTicketDetails.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const user = userEvent.setup()
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.resolved)).toBeInTheDocument()
    expect(screen.getByText(RESOLUTION_COPY.closureNote)).toBeInTheDocument()
    expect(screen.getByText('Çözüldü')).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Müşteriye yanıt ver' }),
    ).not.toBeInTheDocument()

    await waitFor(() => {
      expect(
        screen.getByText(
          'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
        ),
      ).toBeInTheDocument()
    })
    expect(screen.getByText(RESOLUTION_COPY.resolved)).toBeInTheDocument()
    expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
    expect(resolveTicket).toHaveBeenCalledTimes(1)
  })

  it('restores composer and resolve action when refreshed detail is CustomerReplied', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        status: 'Resolved',
        resolvedAt: '2026-07-20T12:00:00.000Z',
        closedByUserId: 'user-1',
      }),
    )
    renderDetail()
    await screen.findByText(RESOLUTION_COPY.closureNote)

    const reopened = sampleDetail({
      status: 'CustomerReplied',
      resolvedAt: null,
      closedByUserId: null,
      messages: [
        ...sampleDetail().messages,
        {
          id: 'msg-3',
          senderType: 'Customer',
          userId: null,
          content: 'Tekrar yazıyorum.',
          isHtml: false,
          createdAt: '2026-07-20T13:00:00.000Z',
        },
      ],
    })
    fetchTicketDetails.mockResolvedValueOnce(reopened)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Yenile' }))

    expect(
      await screen.findByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Müşteriye yanıt ver' }),
    ).toBeInTheDocument()
    expect(screen.queryByText(RESOLUTION_COPY.closureNote)).not.toBeInTheDocument()
    expect(screen.getByText('Müşteri Yanıtladı')).toBeInTheDocument()
    expect(screen.getByText('Tekrar yazıyorum.')).toBeInTheDocument()
  })

  it('keeps timeline and attachment actions available while resolved', async () => {
    fetchTicketDetails.mockResolvedValueOnce(
      sampleDetail({
        status: 'Resolved',
        resolvedAt: '2026-07-20T12:00:00.000Z',
        closedByUserId: 'user-1',
      }),
    )
    renderDetail()

    expect(await screen.findByText(RESOLUTION_COPY.closureNote)).toBeInTheDocument()
    expect(
      screen.getByRole('list', { name: 'Mesaj geçmişi' }),
    ).toBeInTheDocument()
    expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /ekran\.png/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Yenile' })).toBeEnabled()
    expect(
      screen.getByRole('link', { name: 'Destek taleplerine dön' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Çıkış yap' })).toBeEnabled()
  })

  describe('older message history', () => {
    function pagedDetail() {
      return sampleDetail({
        nextMessageCursor: 'cursor-1',
        hasMoreMessages: true,
      })
    }

    const olderPage = {
      messages: [
        {
          id: 'msg-0',
          senderType: 'Customer',
          userId: null,
          content: 'En eski müşteri mesajı',
          isHtml: false,
          createdAt: '2026-07-20T08:00:00.000Z',
        },
      ],
      attachments: [
        {
          id: 'att-0',
          ticketMessageId: 'msg-0',
          fileName: 'eski-log.txt',
          contentType: 'text/plain',
          fileSize: 512,
          createdAt: '2026-07-20T08:00:01.000Z',
        },
      ],
      nextCursor: 'cursor-2',
      hasMore: true,
    }

    const finalOlderPage = {
      messages: [
        {
          id: 'msg-minus-1',
          senderType: 'Support',
          userId: 'user-1',
          content: 'İlk karşılama mesajı',
          isHtml: false,
          createdAt: '2026-07-19T18:00:00.000Z',
        },
      ],
      attachments: [],
      nextCursor: null,
      hasMore: false,
    }

    it('hides the older-history control when the first page already holds everything', async () => {
      fetchTicketDetails.mockResolvedValueOnce(sampleDetail())
      renderDetail()

      await screen.findByRole('heading', { name: 'Mesaj geçmişi' })
      expect(
        screen.queryByRole('button', { name: 'Daha eski mesajları yükle' }),
      ).not.toBeInTheDocument()
      expect(fetchTicketMessages).not.toHaveBeenCalled()
    })

    it('loads older messages before the timeline on demand and keeps focus on the control', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail())
      renderDetail()

      const loadOlder = await screen.findByRole('button', {
        name: 'Daha eski mesajları yükle',
      })
      const timeline = screen.getByRole('list', { name: 'Mesaj geçmişi' })
      expect(
        loadOlder.compareDocumentPosition(timeline) &
          Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy()

      const pending = deferred<typeof olderPage>()
      fetchTicketMessages.mockReturnValueOnce(pending.promise)

      const user = userEvent.setup()
      await user.click(loadOlder)

      expect(fetchTicketMessages).toHaveBeenCalledWith('ticket-1', {
        pageSize: 100,
        cursor: 'cursor-1',
        signal: expect.any(AbortSignal),
      })
      const busy = screen.getByRole('button', {
        name: 'Eski mesajlar yükleniyor…',
      })
      expect(busy).toBeDisabled()
      expect(busy).toHaveAttribute('aria-busy', 'true')

      pending.resolve(olderPage)

      await waitFor(() => {
        expect(
          screen.getByText('En eski müşteri mesajı'),
        ).toBeInTheDocument()
      })

      const articles = within(
        screen.getByRole('list', { name: 'Mesaj geçmişi' }),
      ).getAllByRole('article')
      expect(articles).toHaveLength(3)
      expect(within(articles[0]!).getByText('En eski müşteri mesajı')).toBeInTheDocument()
      expect(within(articles[0]!).getByText('Müşteri')).toBeInTheDocument()
      expect(within(articles[0]!).getByRole('button', { name: /eski-log\.txt/ })).toBeInTheDocument()
      expect(within(articles[1]!).getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
      expect(
        within(articles[2]!).getByText('Merhaba, yardımcı oluyoruz.'),
      ).toBeInTheDocument()

      const afterAppend = screen.getByRole('button', {
        name: 'Daha eski mesajları yükle',
      })
      expect(afterAppend).toBeEnabled()
      expect(afterAppend).toHaveFocus()
    })

    it('hides the control after the final older page arrives', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail())
      renderDetail()
      await screen.findByRole('button', { name: 'Daha eski mesajları yükle' })

      fetchTicketMessages.mockResolvedValueOnce(finalOlderPage)
      const user = userEvent.setup()
      await user.click(
        screen.getByRole('button', { name: 'Daha eski mesajları yükle' }),
      )

      await screen.findByText('İlk karşılama mesajı')
      expect(
        screen.queryByRole('button', { name: 'Daha eski mesajları yükle' }),
      ).not.toBeInTheDocument()

      const articles = within(
        screen.getByRole('list', { name: 'Mesaj geçmişi' }),
      ).getAllByRole('article')
      expect(articles).toHaveLength(3)
      expect(within(articles[0]!).getByText('İlk karşılama mesajı')).toBeInTheDocument()
      expect(within(articles[0]!).getByText('Destek ekibi')).toBeInTheDocument()
    })

    it('keeps the loaded timeline on an older-page failure and offers an accessible retry', async () => {
      fetchTicketDetails.mockResolvedValueOnce(pagedDetail())
      renderDetail()
      await screen.findByRole('button', { name: 'Daha eski mesajları yükle' })

      fetchTicketMessages.mockRejectedValueOnce(new TypeError('Failed to fetch'))
      const user = userEvent.setup()
      await user.click(
        screen.getByRole('button', { name: 'Daha eski mesajları yükle' }),
      )

      const alert = await screen.findByRole('alert')
      expect(alert).toHaveTextContent(
        'Daha eski mesajlar yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.',
      )
      expect(screen.getByText('Merhaba, şifremi unuttum.')).toBeInTheDocument()
      expect(
        screen.queryByRole('button', { name: 'Daha eski mesajları yükle' }),
      ).not.toBeInTheDocument()

      fetchTicketMessages.mockResolvedValueOnce(olderPage)
      await user.click(
        within(alert).getByRole('button', { name: 'Yeniden dene' }),
      )

      await screen.findByText('En eski müşteri mesajı')
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(
        screen.getByRole('button', { name: 'Daha eski mesajları yükle' }),
      ).toBeInTheDocument()
    })
  })
})
