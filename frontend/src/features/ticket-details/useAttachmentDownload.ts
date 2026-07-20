import { useCallback, useEffect, useRef, useState } from 'react'
import { downloadAttachment } from '../../api/attachmentsApi'
import { ApiError } from '../../api/client'
import type { TicketAttachmentMeta } from '../../api/types'

export type AttachmentDownloadErrorKind = 'not-found' | 'network' | 'server'

export type AttachmentDownloadError = {
  attachmentId: string
  kind: AttachmentDownloadErrorKind
}

export type UseAttachmentDownloadResult = {
  activeAttachmentId: string | null
  error: AttachmentDownloadError | null
  download: (attachment: TicketAttachmentMeta) => Promise<void>
  clearError: () => void
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function classifyError(error: unknown): AttachmentDownloadErrorKind {
  if (error instanceof ApiError && error.status === 404) {
    return 'not-found'
  }
  if (error instanceof TypeError) {
    return 'network'
  }
  return 'server'
}

export function useAttachmentDownload(): UseAttachmentDownloadResult {
  const [activeAttachmentId, setActiveAttachmentId] = useState<string | null>(
    null,
  )
  const [error, setError] = useState<AttachmentDownloadError | null>(null)
  const activeController = useRef<AbortController | null>(null)
  const objectUrlRef = useRef<string | null>(null)
  const busyRef = useRef(false)

  const clearError = useCallback(() => {
    setError(null)
  }, [])

  const download = useCallback(async (attachment: TicketAttachmentMeta) => {
    if (busyRef.current) {
      return
    }

    busyRef.current = true
    setActiveAttachmentId(attachment.id)
    setError(null)

    const controller = new AbortController()
    activeController.current = controller

    let objectUrl: string | null = null
    let anchor: HTMLAnchorElement | null = null

    try {
      const blob = await downloadAttachment(attachment.id, {
        signal: controller.signal,
      })

      if (controller.signal.aborted) {
        return
      }

      objectUrl = URL.createObjectURL(blob)
      objectUrlRef.current = objectUrl

      anchor = document.createElement('a')
      anchor.href = objectUrl
      anchor.download = attachment.fileName
      document.body.append(anchor)
      anchor.click()
    } catch (err) {
      if (isAbortError(err) || controller.signal.aborted) {
        return
      }
      if (err instanceof ApiError && err.status === 401) {
        return
      }
      setError({
        attachmentId: attachment.id,
        kind: classifyError(err),
      })
    } finally {
      if (anchor?.isConnected) {
        anchor.remove()
      } else if (anchor) {
        try {
          anchor.remove()
        } catch {
          // ignore cleanup failures for detached nodes
        }
      }

      if (objectUrl) {
        URL.revokeObjectURL(objectUrl)
        if (objectUrlRef.current === objectUrl) {
          objectUrlRef.current = null
        }
      }

      if (activeController.current === controller) {
        activeController.current = null
      }

      busyRef.current = false
      setActiveAttachmentId(null)
    }
  }, [])

  useEffect(() => {
    return () => {
      activeController.current?.abort()
      activeController.current = null
      busyRef.current = false
      if (objectUrlRef.current) {
        URL.revokeObjectURL(objectUrlRef.current)
        objectUrlRef.current = null
      }
    }
  }, [])

  return {
    activeAttachmentId,
    error,
    download,
    clearError,
  }
}
