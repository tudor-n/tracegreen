import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The browser only ever talks to this origin; /api is proxied to the C# API so there is no CORS.
const apiUrl = process.env.API_URL ?? 'http://localhost:8080'
const usePolling = process.env.CHOKIDAR_USEPOLLING === 'true' || process.env.CHOKIDAR_USEPOLLING === '1'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, 'src') },
  },
  server: {
    host: true,
    port: 3000,
    strictPort: true,
    watch: usePolling ? { usePolling: true, interval: 500 } : undefined,
    proxy: {
      '/api': {
        target: apiUrl,
        changeOrigin: true,
        xfwd: true,
        configure: (proxy) => {
          // The API builds verification links from the browser-facing host, not the proxy target.
          proxy.on('proxyReq', (proxyReq, req) => {
            if (req.headers.host) proxyReq.setHeader('x-forwarded-host', req.headers.host)
          })
        },
      },
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
