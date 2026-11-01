import { execSync } from 'node:child_process';
import path from 'node:path';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The version the sidebar shows. Git is the source of truth: `describe` gives the tag on a tagged
// build, the tag plus its distance past it, and -dirty for uncommitted files. OPENFARM_VERSION
// wins because the Docker build dockerignores .git, so CI computes it on the runner.
function resolveAppVersion(): string {
  if (process.env.OPENFARM_VERSION) return process.env.OPENFARM_VERSION;
  try {
    return execSync('git describe --tags --always --dirty', {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    }).trim();
  } catch {
    return 'unknown';
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: { __APP_VERSION__: JSON.stringify(resolveAppVersion()) },
  server: {
    proxy: {
      '/api': 'http://localhost:6868',
      '/hubs': {
        target: 'http://localhost:6868',
        ws: true,
      },
    },
  },
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
});
