import { apiRequest } from './client'
import type { Parameter, ParameterChangeLog } from './types'

export type ListParametersOptions = {
  signal?: AbortSignal
}

export type UpdateParameterOptions = {
  signal?: AbortSignal
}

export type ListParameterAuditOptions = {
  take?: number
  key?: string
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

export function listParameterAudit(
  options: ListParameterAuditOptions = {},
): Promise<ParameterChangeLog[]> {
  const params = new URLSearchParams()
  if (options.take !== undefined) {
    params.set('take', String(options.take))
  }
  if (options.key !== undefined && options.key.trim() !== '') {
    params.set('key', options.key.trim())
  }
  const query = params.toString()
  return apiRequest<ParameterChangeLog[]>(
    `/api/parameters/audit${query ? `?${query}` : ''}`,
    {
      signal: options.signal,
    },
  )
}
