import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { ParameterChangeLog } from '../../api/types'
import { useParameterAudit } from './useParameterAudit'

const listParameterAudit = vi.hoisted(() => vi.fn())

vi.mock('../../api/parametersApi', () => ({
  listParameterAudit,
}))

const sampleAudit: ParameterChangeLog[] = [
  {
    id: 'log-1',
    parameterKey: 'AutoResolve.InactiveDays',
    oldValue: '3',
    newValue: '5',
    changedByUserId: 'user-1',
    changedByUsername: 'admin',
    changedAt: '2026-07-21T13:00:00.000Z',
  },
]

describe('useParameterAudit', () => {
  beforeEach(() => {
    listParameterAudit.mockReset()
  })

  it('loads last N audit rows', async () => {
    listParameterAudit.mockResolvedValueOnce(sampleAudit)

    const { result } = renderHook(() => useParameterAudit(20))

    await waitFor(() => {
      expect(result.current.hasLoaded).toBe(true)
    })

    expect(result.current.entries).toEqual(sampleAudit)
    expect(listParameterAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        take: 20,
        signal: expect.any(AbortSignal),
      }),
    )
    expect(result.current.error).toBeNull()
  })

  it('classifies network errors', async () => {
    listParameterAudit.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const { result } = renderHook(() => useParameterAudit())

    await waitFor(() => {
      expect(result.current.error).toBe('network')
    })
  })

  it('ignores protected 401 as page error', async () => {
    listParameterAudit.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))

    const { result } = renderHook(() => useParameterAudit())

    await waitFor(() => {
      expect(listParameterAudit).toHaveBeenCalled()
    })

    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.hasLoaded).toBe(false)
  })
})
