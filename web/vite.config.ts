/// <reference types="vitest" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// The SPA calls relative /api/... URLs; in dev and preview Vite proxies them to the API,
// so the browser sees one origin and the API needs no CORS setup.
// Override the target with VITE_API_TARGET (e.g. in .env.local) if the API runs elsewhere.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', ''); // '.' = the web/ folder (where npm scripts run)
  const target = env.VITE_API_TARGET || 'http://localhost:5050';
  const proxy = { '/api': { target, changeOrigin: true } };

  return {
    plugins: [react()],
    server: { port: 5173, proxy },
    preview: { port: 4173, proxy },
    test: { environment: 'node' },
  };
});
