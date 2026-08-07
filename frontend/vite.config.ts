import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// SPA only — no Next/Nuxt. Talks to ASP.NET Core over REST.
// Dev: relative /api and /health → Vite proxy (empty VITE_API_BASE_URL).
const securityHeaders = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Content-Security-Policy':
    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'",
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
}

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    headers: securityHeaders,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5154', changeOrigin: true },
      '/health': { target: 'http://127.0.0.1:5154', changeOrigin: true },
    },
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    headers: securityHeaders,
    proxy: {
      '/api': { target: 'http://127.0.0.1:8080', changeOrigin: true },
      '/health': { target: 'http://127.0.0.1:8080', changeOrigin: true },
    },
  },
})
