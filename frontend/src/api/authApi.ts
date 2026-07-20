import { apiRequest } from './client'
import type { LoginRequest, LoginResponse } from './types'

export function login(request: LoginRequest): Promise<LoginResponse> {
  return apiRequest<LoginResponse>('/api/auth/login', {
    method: 'POST',
    body: request,
    auth: false,
    skipAuthRedirect: true,
  })
}
