import { beforeEach, describe, expect, it, vi } from 'vitest'
import { downloadAttachment } from './attachmentsApi'

describe('downloadAttachment', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('GETs /api/attachments/{encoded-id} with AbortSignal and bearer header', async () => {
    const controller = new AbortController()
    sessionStorage.setItem('vshd.accessToken', 'download-token')
    const bytes = new TextEncoder().encode('file-bytes')
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(bytes, {
        status: 200,
        headers: { 'Content-Type': 'text/plain' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await downloadAttachment('att/99', {
      signal: controller.signal,
    })

    expect(result).toBeInstanceOf(Blob)
    expect(new TextDecoder().decode(await result.arrayBuffer())).toBe(
      'file-bytes',
    )
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      `/api/attachments/${encodeURIComponent('att/99')}`,
    )
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: 'GET',
      signal: controller.signal,
    })
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer download-token')
    expect(headers.has('Content-Type')).toBe(false)
    expect(String(fetchMock.mock.calls[0]?.[0])).not.toContain('download-token')
  })
})
