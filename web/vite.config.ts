import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5175,
    // The .NET API (src/Kultur.Api, http profile). Same-origin in dev and prod, so no CORS.
    proxy: {
      '/api': { target: 'http://localhost:5280', changeOrigin: false },
    },
  },
});
