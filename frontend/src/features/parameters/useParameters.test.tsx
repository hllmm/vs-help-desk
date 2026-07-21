import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Parameter } from '../../api/types'
import { useParameters } from './useParameters'

const listParameters = vi.hoisted(() => vi.fn())
const updateParameter = vi.hoisted(() => vi.fn())

vi.mock('../../api/parametersApi', () => ({
  listParameters,
  updateParameter,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const sampleParameters: Parameter[] = [
  {
    key: 'AutoResolve.InactiveDays',
    value: '3',
    description: 'WaitingCustomerReply sonrası otomatik çözüm eşiği (gün)',
    updatedAt: '2026-07-21T12:00:00.000Z',
  },
]

describe('useParameters', () => {
  beforeEach(() => {
    listParameters.mockReset()
    updateParameter.mockReset()
  })

  it('moves from initial loading to loaded data', async () => {
    const pending = deferred<Parameter[]>()
    listParameters.mockReturnValueOnce(pending.promise)

    const { result } = renderHook(() => useParameters())

    expect(result.current.isInitialLoading).toBe(true)
    expect(result.current.hasLoaded).toBe(false)
    expect(result.current.parameters).toEqual([])

    await act(async () => {
      pending.resolve(sampleParameters)
    })

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.isInitialLoading).toBe(false)
    expect(result.current.parameters).toEqual(sampleParameters)
    expect(result.current.error).toBeNull()
  })

  it('classifies TypeError as network and ApiError as server', async () => {
    listParameters.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const { result } = renderHook(() => useParameters())

    await waitFor(() => {
      expect(result.current.error).toBe('network')
    })

    listParameters.mockRejectedValueOnce(new ApiError(500, 'Server exploded'))

    await act(async () => {
      await result.current.refresh()
    })

    await waitFor(() => {
      expect(result.current.error).toBe('server')
    })
  })

  it('does not expose protected 401 as a page error', async () => {
    listParameters.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))

    const { result } = renderHook(() => useParameters())

    await waitFor(() => {
      expect(listParameters).toHaveBeenCalled()
    })

    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.hasLoaded).toBe(false)
  })

  it('saveParameter updates the matching row on success', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    const updated: Parameter = {
      ...sampleParameters[0]!,
      value: '7',
      updatedAt: '2026-07-21T13:00:00.000Z',
    }
    updateParameter.mockResolvedValueOnce(updated)

    const { result } = renderHook(() => useParameters())

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    let saveResult!: Awaited<ReturnType<typeof result.current.saveParameter>>
    await act(async () => {
      saveResult = await result.current.saveParameter(
        'AutoResolve.InactiveDays',
        '7',
      )
    })

    expect(saveResult).toEqual({ ok: true, parameter: updated })
    expect(result.current.parameters[0]?.value).toBe('7')
    expect(updateParameter).toHaveBeenCalledWith(
      'AutoResolve.InactiveDays',
      '7',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })

  it('maps validation and network save failures', async () => {
    listParameters.mockResolvedValueOnce(sampleParameters)
    const { result } = renderHook(() => useParameters())

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    updateParameter.mockRejectedValueOnce(
      new ApiError(400, 'A domain rule was violated.'),
    )
    let validation!: Awaited<ReturnType<typeof result.current.saveParameter>>
    await act(async () => {
      validation = await result.current.saveParameter(
        'AutoResolve.InactiveDays',
        '0',
      )
    })
    expect(validation).toEqual({ ok: false, error: 'validation' })

    updateParameter.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    let network!: Awaited<ReturnType<typeof result.current.saveParameter>>
    await act(async () => {
      network = await result.current.saveParameter(
        'AutoResolve.InactiveDays',
        '5',
      )
    })
    expect(network).toEqual({ ok: false, error: 'network' })
  })
})
