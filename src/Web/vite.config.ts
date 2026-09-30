/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5180'

/**
 * O mesmo encaminhamento serve o servidor de desenvolvimento e a pré-visualização do que foi
 * compilado. A sessão vive num cookie `HttpOnly` e `SameSite=Lax`, portanto o browser tem de ver a
 * API e a interface na mesma origem — sem isto, uma pré-visualização pede a API noutra origem e a
 * sessão nunca chega lá.
 */
const proxy = {
  '/api': { target: apiTarget, changeOrigin: false },
  '/health': { target: apiTarget, changeOrigin: false },
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': new URL('./src', import.meta.url).pathname },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy,
  },

  // A pré-visualização serve os ficheiros compilados, sem transformação a pedido nem ligação de
  // recarregamento aberta. É o que se entrega, e é contra isso que os testes de ponta a ponta devem
  // correr: uma suíte verde contra o servidor de desenvolvimento não diz nada sobre o que foi
  // compilado. Mesmo porto de propósito, para o endereço dos testes não mudar.
  preview: {
    port: 5173,
    strictPort: true,
    proxy,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
  },
})
