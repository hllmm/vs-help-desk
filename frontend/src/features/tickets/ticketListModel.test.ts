import { describe, expect, it } from 'vitest'
import type { TicketListItem } from '../../api/types'
import {
  countTicketsByStatus,
  filterTicketsByStatus,
  formatTicketActivity,
  getTicketFilterLabel,
  getTicketStatusMeta,
  searchTickets,
  TICKET_STATUS_FILTERS,
} from './ticketListModel'

function ticket(
  overrides: Partial<TicketListItem> & Pick<TicketListItem, 'id' | 'status'>,
): TicketListItem {
  return {
    ticketNumber: `VS-${overrides.id.padStart(6, '0')}`,
    subject: `Subject ${overrides.id}`,
    customerName: `Customer ${overrides.id}`,
    customerEmail: `customer${overrides.id}@example.com`,
    lastActivityAt: '2026-07-20T10:00:00.000Z',
    assignedUserId: null,
    ...overrides,
  }
}

const items: TicketListItem[] = [
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
    status: 'WaitingCustomerReply',
    customerName: 'Ali Demir',
    customerEmail: 'ali@example.com',
  }),
  ticket({
    id: '3',
    status: 'CustomerReplied',
    subject: 'Fatura sorunu',
  }),
  ticket({
    id: '4',
    status: 'Resolved',
  }),
  ticket({
    id: '5',
    status: 'Escalated',
    ticketNumber: 'VS-000099',
    subject: 'Unknown path',
  }),
]

describe('getTicketStatusMeta', () => {
  it('maps known statuses to Turkish labels and tones', () => {
    expect(getTicketStatusMeta('New')).toEqual({
      label: 'Yeni',
      tone: 'new',
    })
    expect(getTicketStatusMeta('WaitingCustomerReply')).toEqual({
      label: 'Müşteri Bekleniyor',
      tone: 'waiting',
    })
    expect(getTicketStatusMeta('CustomerReplied')).toEqual({
      label: 'Müşteri Yanıtladı',
      tone: 'replied',
    })
    expect(getTicketStatusMeta('Resolved')).toEqual({
      label: 'Çözüldü',
      tone: 'resolved',
    })
  })

  it('keeps unknown statuses readable', () => {
    expect(getTicketStatusMeta('Escalated')).toEqual({
      label: 'Escalated',
      tone: 'unknown',
    })
  })
})

describe('getTicketFilterLabel', () => {
  it('labels filters in Turkish including Tümü', () => {
    expect(getTicketFilterLabel('all')).toBe('Tümü')
    expect(getTicketFilterLabel('New')).toBe('Yeni')
    expect(getTicketFilterLabel('WaitingCustomerReply')).toBe(
      'Müşteri Bekleniyor',
    )
    expect(getTicketFilterLabel('CustomerReplied')).toBe('Müşteri Yanıtladı')
    expect(getTicketFilterLabel('Resolved')).toBe('Çözüldü')
  })

  it('exposes the stable filter order', () => {
    expect(TICKET_STATUS_FILTERS).toEqual([
      'all',
      'New',
      'WaitingCustomerReply',
      'CustomerReplied',
      'Resolved',
    ])
  })
})

describe('searchTickets', () => {
  it('matches Turkish mixed-case customer names', () => {
    expect(searchTickets(items, 'İREM')).toContainEqual(items[0])
  })

  it('matches ticket numbers case-insensitively', () => {
    expect(searchTickets(items, 'vs-000042')).toContainEqual(items[0])
  })

  it('matches subject and email fields', () => {
    expect(searchTickets(items, 'fatura')).toContainEqual(items[2])
    expect(searchTickets(items, 'ali@example.com')).toContainEqual(items[1])
  })

  it('trims the query and returns all items for blank search', () => {
    expect(searchTickets(items, '   ')).toEqual(items)
    expect(searchTickets(items, '')).toEqual(items)
  })
})

describe('filterTicketsByStatus', () => {
  it('returns all items for the all filter', () => {
    expect(filterTicketsByStatus(items, 'all')).toEqual(items)
  })

  it('filters by exact known status', () => {
    expect(filterTicketsByStatus(items, 'New')).toEqual([items[0]])
    expect(filterTicketsByStatus(items, 'Resolved')).toEqual([items[3]])
  })
})

describe('countTicketsByStatus', () => {
  it('counts after the provided collection (search, before status filter)', () => {
    expect(countTicketsByStatus(items)).toEqual({
      all: items.length,
      New: 1,
      WaitingCustomerReply: 1,
      CustomerReplied: 1,
      Resolved: 1,
    })
  })

  it('counts only searched items when given a subset', () => {
    const searched = searchTickets(items, 'İREM')
    expect(countTicketsByStatus(searched)).toEqual({
      all: 1,
      New: 1,
      WaitingCustomerReply: 0,
      CustomerReplied: 0,
      Resolved: 0,
    })
  })
})

describe('formatTicketActivity', () => {
  it('returns the original string for invalid dates', () => {
    expect(formatTicketActivity('not-a-date')).toBe('not-a-date')
  })

  it('formats valid ISO timestamps with tr-TR medium date and short time', () => {
    const iso = '2026-07-20T10:15:00.000Z'
    const expected = new Intl.DateTimeFormat('tr-TR', {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(iso))
    expect(formatTicketActivity(iso)).toBe(expected)
  })
})
