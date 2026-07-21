import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { listParameterAudit } from '../../api/parametersApi'
import type { ParameterChangeLog } from '../../api/types'

export type ParameterAuditLoadErrorKind = 'network' | 'server'

export type UseParameterAuditResult = {
  entries: readonly ParameterChangeLog[]
  hasLoaded: boolean
  isLoading: boolean
  error: ParameterAuditLoadErrorKind | null
  refresh: () => Promise<void>
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

const DEFAULT_TAKE = 20

export function useParameterAudit(
  take: number = DEFAULT_TAKE,
): UseParameterAuditResult {
  const [entries, setEntries] = useState<ParameterChangeLog[]>([])
  const [hasLoaded, setHasLoaded] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<ParameterAuditLoadErrorKind | null>(null)
  const requestSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    const sequence = ++requestSequence.current
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller
    setIsLoading(true)
    setError(null)

    try {
      const rows = await listParameterAudit({
        take,
        signal: controller.signal,
      })
      if (sequence === requestSequence.current) {
        setEntries(rows)
        setHasLoaded(true)
        setIsLoading(false)
        setError(null)
      }
    } catch (loadError) {
      if (sequence !== requestSequence.current || isAbortError(loadError)) {
        return
      }
      if (loadError instanceof ApiError && loadError.status === 401) {
        return
      }
      setHasLoaded(true)
      setIsLoading(false)
      setError(loadError instanceof TypeError ? 'network' : 'server')
    }
  }, [take])

  useEffect(() => {
    void load()
    return () => {
      activeController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  return {
    entries,
    hasLoaded,
    isLoading,
    error,
    refresh: load,
  }
}
