/**
 * Post-login return path: only same-app relative paths.
 * Rejects protocol-relative and absolute URLs.
 */
export function resolveSafeReturnPath(
  candidate: string | undefined | null,
  fallback = '/tickets',
): string {
  if (candidate == null || candidate === '' || candidate === '/login') {
    return fallback
  }

  const path = candidate.trim()
  if (!path.startsWith('/')) {
    return fallback
  }
  if (path.startsWith('//') || path.includes('://')) {
    return fallback
  }
  // Disallow backslash tricks and control characters
  if (path.includes('\\') || /[\u0000-\u001f]/.test(path)) {
    return fallback
  }

  return path
}
