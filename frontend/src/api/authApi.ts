import { apiRequest } from './client'
import type { CurrentUser, LoginRequest, LoginResponse } from './types'

export function login(request: LoginRequest): Promise<LoginResponse> {
  return apiRequest<LoginResponse>('/api/auth/login', {
    method: 'POST',
    body: request,
    auth: false,
    skipAuthRedirect: true,
  })
}

export function logout(): Promise<void> {
  return apiRequest<void>('/api/auth/logout', {
    method: 'POST',
    skipAuthRedirect: true,
  })
}

export function fetchCurrentUser(): Promise<CurrentUser> {
  return apiRequest<CurrentUser>('/api/auth/me', {
    skipAuthRedirect: true,
  })
}
