import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// base: /admin/ car servi sous ce chemin par ShopAuth en prod (wwwroot/admin).
// build.outDir : le build atterrit directement dans le dossier que ShopAuth sert.
// proxy : en dev, meme origine cote navigateur -> pas de souci CORS/cookie.
export default defineConfig({
  base: '/admin/',
  plugins: [react()],
  build: {
    outDir: '../wwwroot/admin',
    emptyOutDir: true,
  },
  server: {
    port: 5174,
    proxy: {
      '/admin/api': 'http://localhost:5124',
      '/api/account': 'http://localhost:5124',
    },
  },
})
