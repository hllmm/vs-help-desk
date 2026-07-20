import { apiRequest } from './client'
import type { Parameter } from './types'

export type ListParametersOptions = {
  signal?: AbortSignal
}

export type UpdateParameterOptions = {
  signal?: AbortSignal
}

export function listParameters(
  options: ListParametersOptions = {},
): Promise<Parameter[]> {
  return apiRequest<Parameter[]>('/api/parameters', {
    signal: options.signal,
  })
}

export function updateParameter(
  key: string,
  value: string,
  options: UpdateParameterOptions = {},
): Promise<Parameter> {
  return apiRequest<Parameter>(
    `/api/parameters/${encodeURIComponent(key)}`,
    {
      method: 'PUT',
      body: { value },
      signal: options.signal,
    },
  )
}
