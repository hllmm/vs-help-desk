import { describe, expect, it } from 'vitest'
import {
  formatTicketActivity,
  getTicketFilterLabel,
  getTicketStatusMeta,
  TICKET_STATUS_FILTERS,
} from './ticketListModel'

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

  it('maps unknown statuses to Turkish fallback label', () => {
    expect(getTicketStatusMeta('Escalated')).toEqual({
      label: 'Bilinmeyen durum',
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
