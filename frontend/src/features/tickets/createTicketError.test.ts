import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/client'
import { createTicketErrorMessage } from './createTicketError'

describe('createTicketErrorMessage', () => {
  it.each([
    ['portal-idempotency-payload-conflict', 'Bu oluşturma anahtarı farklı içerikle kullanılmış. Formu değiştirip yeniden deneyin.'],
    ['portal-ticket-customer-email-invalid', 'Geçerli bir müşteri e-posta adresi girin.'],
    ['portal-ticket-content-too-long', 'İçerik en fazla 262144 karakter olabilir.'],
  ])('maps %s to Turkish copy', (code, expected) => {
    expect(createTicketErrorMessage(
      new ApiError(409, 'Request failed (409)', { code }),
    )).toBe(expected)
  })

  it('maps network and server failures without exposing raw text', () => {
    expect(createTicketErrorMessage(new TypeError('Failed to fetch')))
      .toBe('Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.')
    expect(createTicketErrorMessage(new ApiError(500, 'raw upstream detail')))
      .toBe('Talep oluşturulamadı. Lütfen yeniden deneyin.')
  })
})
