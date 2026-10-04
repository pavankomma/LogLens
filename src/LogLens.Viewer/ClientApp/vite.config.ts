import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * Builds straight into ../wwwroot, which the viewer serves as static files.
 *
 * `npm run dev` proxies /api to a running viewer on 5341 (start one with
 * `dotnet run --project src/LogLens.Viewer -- --demo`), so the front end hot-reloads without the
 * host being rebuilt. The proxy covers /api/stream too — SSE live tail is the thing most worth
 * having live while iterating on the UI.
 */
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    outDir: '../wwwroot',
    // Owned entirely by this build, so a stale bundle can never be served alongside a fresh one.
    emptyOutDir: true,
    // The app is served from a loopback service and read by nobody but us; source maps make a
    // production stack trace in the operator's console actionable.
    sourcemap: true,
    target: 'es2022',
    rollupOptions: {
      output: {
        // Stable filenames, deliberately no content hash.
        //
        // The .NET Web SDK globs wwwroot during *evaluation*, before any target can run, so it
        // captures whatever filenames are on disk at that moment. Hashed names change on every
        // build, and the SDK then fails resolving assets that Vite has already replaced. Hashes
        // exist to make assets immutable behind a CDN; this is a loopback service serving from
        // disk with ETags, so the browser revalidates for free and the hash earns nothing.
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5341',
    },
  },
})
