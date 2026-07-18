import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: Vite serves the app and proxies /api to the local API.
// Prod: `npm run build` emits into the API's wwwroot so ASP.NET serves it
// and the app is same-origin with the API (no CORS in production).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': 'http://localhost:5000' },
  },
  build: {
    outDir: '../backend/KestrelBooks.Api/wwwroot',
    emptyOutDir: true,
  },
});
