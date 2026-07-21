import { apiBlobRequest } from './client'

export type DownloadAttachmentOptions = {
  signal?: AbortSignal
}

export function downloadAttachment(
  attachmentId: string,
  options: DownloadAttachmentOptions = {},
): Promise<Blob> {
  return apiBlobRequest(
    `/api/attachments/${encodeURIComponent(attachmentId)}`,
    { signal: options.signal },
  )
}
