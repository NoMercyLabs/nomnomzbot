// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
    tailwindcss(),
  ],
  // 5090 is the registered OAuth origin; the proxy keeps the refresh cookie same-origin.
  // xfwd tells the bot the address the browser used (a phone on the network is not localhost), so its
  // sign-in callback and its refresh origin check match that address.
  server: {
    port: 5090,
    strictPort: true,
    // The https tunnel to this dev server, so a phone signs in by redirect.
    allowedHosts: ['vue.nomnomz.bot'],
    proxy: {
      '/api': { target: 'http://localhost:5080', xfwd: true },
      '/hubs': { target: 'http://localhost:5080', ws: true, xfwd: true },
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
