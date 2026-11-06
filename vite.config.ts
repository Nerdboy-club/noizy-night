import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  server: {
    port: 3000,
    open: '/playground/'
  },
  optimizeDeps: {
    exclude: ['@dimforge/rapier3d-compat']
  },
  plugins: [
    {
      name: 'playground-rewrite',
      configureServer(server) {
        server.middlewares.use((req, _res, next) => {
          if (req.url === '/playground' || req.url === '/playground/') {
            req.url = '/playground/index.html';
          }
          next();
        });
      }
    }
  ],
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        playground: resolve(__dirname, 'playground/index.html')
      }
    }
  }
});
