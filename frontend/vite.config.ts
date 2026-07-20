import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// SPA only — no Next/Nuxt. Talks to ASP.NET Core over REST.
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
  },
})
