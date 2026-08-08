import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { listParameters, updateParameter } from '../../api/parametersApi'
import type { Parameter } from '../../api/types'

export type ParameterLoadErrorKind = 'network' | 'server'

export type ParameterSaveErrorKind =
  | 'validation'
  | 'not-found'
  | 'network'
  | 'server'

export type ParameterSaveResult =
  | { ok: true; parameter: Parameter }
  | { ok: false; error: ParameterSaveErrorKind | null }

export type UseParametersResult = {
  parameters: readonly Parameter[]
  hasLoaded: boolean
  isInitialLoading: boolean
  isRefreshing: boolean
  error: ParameterLoadErrorKind | null
  refresh: () => Promise<void>
  savingKey: string | null
  saveParameter: (key: string, value: string) => Promise<ParameterSaveResult>
}

type ParametersRequestState = {
  parameters: Parameter[]
  hasLoaded: boolean
  isLoading: boolean
  error: ParameterLoadErrorKind | null
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function mapSaveError(error: unknown): ParameterSaveErrorKind | null {
  if (isAbortError(error)) {
    return null
  }
  if (error instanceof ApiError && error.status === 401) {
    return null
  }
  if (error instanceof TypeError) {
    return 'network'
  }
  if (error instanceof ApiError) {
    if (error.status === 400) {
      return 'validation'
    }
    if (error.status === 404) {
      return 'not-found'
    }
    return 'server'
  }
  return 'server'
}

export function useParameters(): UseParametersResult {
  const [state, setState] = useState<ParametersRequestState>({
    parameters: [],
    hasLoaded: false,
    isLoading: false,
    error: null,
  })
  const [savingKey, setSavingKey] = useState<string | null>(null)
  const requestSequence = useRef(0)
  const saveSequence = useRef(0)
  const activeController = useRef<AbortController | null>(null)
  const saveController = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    const sequence = ++requestSequence.current
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller
    setState((current) => ({ ...current, isLoading: true, error: null }))

    try {
      const parameters = await listParameters({ signal: controller.signal })
      if (sequence === requestSequence.current) {
        setState({
          parameters,
          hasLoaded: true,
          isLoading: false,
          error: null,
        })
      }
    } catch (error) {
      if (sequence !== requestSequence.current || isAbortError(error)) {
        return
      }
      if (error instanceof ApiError && error.status === 401) {
        return
      }
      setState((current) => ({
        ...current,
        hasLoaded: true,
        isLoading: false,
        error: error instanceof TypeError ? 'network' : 'server',
      }))
    }
  }, [])

  useEffect(() => {
    const id = setTimeout(() => void load(), 0)
    return () => {
      clearTimeout(id)
      activeController.current?.abort()
      saveController.current?.abort()
      requestSequence.current += 1
    }
  }, [load])

  const saveParameter = useCallback(
    async (key: string, value: string): Promise<ParameterSaveResult> => {
      const sequence = ++saveSequence.current
      saveController.current?.abort()
      const controller = new AbortController()
      saveController.current = controller
      setSavingKey(key)

      try {
        const parameter = await updateParameter(key, value, {
          signal: controller.signal,
        })
        if (sequence === saveSequence.current) {
          setState((current) => ({
            ...current,
            parameters: current.parameters.map((item) =>
              item.key === key ? parameter : item,
            ),
          }))
        }
        return { ok: true, parameter }
      } catch (error) {
        return { ok: false, error: mapSaveError(error) }
      } finally {
        if (sequence === saveSequence.current) {
          saveController.current = null
          setSavingKey(null)
        }
      }
    },
    [],
  )

  return {
    parameters: state.parameters,
    hasLoaded: state.hasLoaded,
    isInitialLoading: state.isLoading && !state.hasLoaded,
    isRefreshing: state.isLoading && state.hasLoaded,
    error: state.error,
    refresh: load,
    savingKey,
    saveParameter,
  }
}
