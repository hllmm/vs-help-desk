import { describe, expect, it } from 'vitest'
import type { TicketAttachmentMeta } from '../../api/types'
import {
  formatAttachmentSize,
  formatTicketDetailDate,
  getMessageSenderMeta,
  groupAttachmentsByMessage,
} from './ticketDetailModel'

function attachment(
  overrides: Partial<TicketAttachmentMeta> &
    Pick<TicketAttachmentMeta, 'id' | 'ticketMessageId' | 'fileName'>,
): TicketAttachmentMeta {
  return {
    contentType: 'text/plain',
    fileSize: 10,
    createdAt: '2026-07-20T10:00:00.000Z',
    ...overrides,
  }
}

describe('groupAttachmentsByMessage', () => {
  it('keeps server order within each message and does not mutate input', () => {
    const input: TicketAttachmentMeta[] = [
      attachment({ id: 'a1', ticketMessageId: 'm1', fileName: 'one.txt' }),
      attachment({ id: 'a2', ticketMessageId: 'm2', fileName: 'two.txt' }),
      attachment({ id: 'a3', ticketMessageId: 'm1', fileName: 'three.txt' }),
    ]
    const snapshot = structuredClone(input)

    const groups = groupAttachmentsByMessage(input)

    expect(groups.get('m1')?.map((item) => item.id)).toEqual(['a1', 'a3'])
    expect(groups.get('m2')?.map((item) => item.id)).toEqual(['a2'])
    expect(input).toEqual(snapshot)
  })
})

describe('getMessageSenderMeta', () => {
  it('maps known senders to labels and semantic tones', () => {
    expect(getMessageSenderMeta('Customer')).toEqual({
      label: 'Müşteri',
      tone: 'customer',
    })
    expect(getMessageSenderMeta('Support')).toEqual({
      label: 'Destek ekibi',
      tone: 'support',
    })
    expect(getMessageSenderMeta('System')).toEqual({
      label: 'Sistem',
      tone: 'system',
    })
    expect(getMessageSenderMeta('')).toEqual({
      label: 'Gönderen bilgisi yok',
      tone: 'unknown',
    })
  })
})

describe('formatTicketDetailDate', () => {
  it('returns the original string for invalid dates', () => {
    expect(formatTicketDetailDate('not-a-date')).toBe('not-a-date')
  })

  it('formats valid ISO timestamps with tr-TR', () => {
    const iso = '2026-07-20T10:15:00.000Z'
    const expected = new Intl.DateTimeFormat('tr-TR', {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(iso))
    expect(formatTicketDetailDate(iso)).toBe(expected)
  })
})

describe('formatAttachmentSize', () => {
  it('formats zero, bytes, KiB, and MiB without NaN or Infinity', () => {
    expect(formatAttachmentSize(0)).toBe('0 B')
    expect(formatAttachmentSize(500)).toBe('500 B')
    expect(formatAttachmentSize(1024)).toBe('1 KiB')
    expect(formatAttachmentSize(1536)).toMatch(/1([.,]5)? KiB/)
    expect(formatAttachmentSize(1024 * 1024)).toBe('1 MiB')
    expect(formatAttachmentSize(2.5 * 1024 * 1024)).toMatch(/2([.,]5)? MiB/)
  })

  it('handles negative and invalid input safely', () => {
    for (const value of [-1, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY]) {
      const formatted = formatAttachmentSize(value)
      expect(formatted).not.toMatch(/NaN|Infinity/i)
      expect(formatted.length).toBeGreaterThan(0)
    }
  })
})
