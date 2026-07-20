/** Read double-submit CSRF token from the non-HttpOnly `vshd.csrf` cookie. */
export function getCsrfToken(): string | null {
  const match = document.cookie.match(/(?:^|; )vshd\.csrf=([^;]*)/)
  return match ? decodeURIComponent(match[1]) : null
}
