import { apiBlobRequest, apiFormRequest } from './client'

export type DownloadAttachmentOptions = {
  signal?: AbortSignal
}

export type UploadAttachmentResult = {
  id: string
  ticketMessageId: string
  fileName: string
  fileSize: number
  contentType: string
  uploadedAt: string
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

export function uploadAttachment(
  messageId: string,
  file: File,
  options: DownloadAttachmentOptions = {},
): Promise<UploadAttachmentResult> {
  const formData = new FormData()
  formData.append('file', file)
  return apiFormRequest<UploadAttachmentResult>(
    `/api/ticket-messages/${encodeURIComponent(messageId)}/attachments`,
    formData,
    { signal: options.signal },
  )
}
