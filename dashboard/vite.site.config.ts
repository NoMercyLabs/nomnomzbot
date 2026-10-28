// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { resolve } from 'node:path';

import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

import { designerIcons } from './vite-plugins/designerIcons';
import { siteFiles } from './vite-plugins/siteFiles';

// The public site: the home page, the "Coming soon" login and a not-found page, as static files for
// GitHub Pages (`npm run build-site`, mode `site`, output dist-site). It has no service worker and no web
// manifest: the site is static pages the host already caches, an offline copy of a marketing page is not
// worth it, and a worker would keep serving an old page if this domain later moves back to the app.
export const siteDomain: string = 'nomnomz.bot';

// The router's real pages besides the home page: each gets its own file, so the host answers 200 for it.
export const sitePages = ['login'] as const;

export default defineConfig({
  plugins: [vue(), tailwindcss(), designerIcons(), siteFiles(siteDomain, sitePages)],
  build: {
    outDir: 'dist-site',
    emptyOutDir: true,
    rollupOptions: { input: resolve('site.html') },
  },
  resolve: {
    alias: {
      '@': resolve('src'),
    },
  },
});
