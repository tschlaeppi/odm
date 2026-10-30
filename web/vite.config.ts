import { svelte } from '@sveltejs/vite-plugin-svelte'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [svelte()],
  server: {
    // Entwicklung: API-Aufrufe an das lokale Backend weiterleiten
    proxy: { '/api': 'http://localhost:8080' },
  },
})
