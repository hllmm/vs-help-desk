import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// SPA only — no Next/Nuxt. Talks to ASP.NET Core over REST.
// Dev: relative /api and /health → Vite proxy (empty VITE_API_BASE_URL).
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5154', changeOrigin: true },
      '/health': { target: 'http://127.0.0.1:5154', changeOrigin: true },
    },
  },
})
