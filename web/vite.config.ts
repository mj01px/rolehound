import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Same-origin calls in development; the API runs on port 5080.
      '/api': {
        target: 'http://localhost:5080',
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
