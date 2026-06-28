import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// The backend ASP.NET Core host (see start.ps1 / launchSettings) listens here.
const BACKEND = process.env.BACKEND_URL ?? 'http://localhost:5180'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // When running Vite-led (`npm run dev` directly), proxy API calls to the backend.
    // Under SpaProxy (dotnet-led), the browser hits the backend origin and this is unused.
    proxy: {
      '/api': { target: BACKEND, changeOrigin: true },
    },
  },
  build: {
    // "Run for real": emit the SPA where the .NET host serves it from wwwroot.
    outDir: '../backend/src/Web/wwwroot',
    emptyOutDir: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./tests/setup.ts'],
    css: true,
    include: ['tests/**/*.{test,spec}.{ts,tsx}'],
  },
})
