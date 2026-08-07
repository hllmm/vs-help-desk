import { ApiError } from '../../api/client'

function errorCode(error: ApiError): string | null {
  if (typeof error.body !== 'object' || error.body === null || !('code' in error.body)) {
    return null
  }
  const code = (error.body as { code: unknown }).code
  return typeof code === 'string' ? code : null
}

export function createTicketErrorMessage(error: unknown): string {
  if (error instanceof TypeError) {
    return 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
  }

  if (error instanceof ApiError) {
    switch (errorCode(error)) {
      case 'portal-idempotency-payload-conflict':
        return 'Bu oluşturma anahtarı farklı içerikle kullanılmış. Formu değiştirip yeniden deneyin.'
      case 'portal-ticket-customer-email-invalid':
        return 'Geçerli bir müşteri e-posta adresi girin.'
      case 'portal-ticket-content-too-long':
        return 'İçerik en fazla 262144 karakter olabilir.'
      case 'portal-ticket-payload-invalid':
        return 'Form alanlarından biri izin verilen uzunluğu aşıyor.'
    }

    if (error.status === 429) {
      return 'Çok fazla istek gönderildi. Lütfen biraz sonra yeniden deneyin.'
    }
  }

  return 'Talep oluşturulamadı. Lütfen yeniden deneyin.'
}
