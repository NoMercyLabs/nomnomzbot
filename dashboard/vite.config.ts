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

import { defineConfig, type ProxyOptions } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'
import Icons from 'unplugin-icons/vite'
import { FileSystemIconLoader } from 'unplugin-icons/loaders'

// The designer's icon pack (Line style), imported as `~icons/nnz/<name>`. The files carry a baked stroke
// color; it becomes currentColor so an icon takes the text color like any other glyph.
const designerIcons = FileSystemIconLoader('./src/assets/icons/line', (svg: string): string =>
  svg
    .replace(/\sstyle="[^"]*"/g, '')
    .replace(/stroke="#11181C"/gi, 'stroke="currentColor"')
    .replace(/fill="#11181C"/gi, 'fill="currentColor"'),
)

// The bot the dev server talks to. NNZ_API points it at another bot, such as the live box, for real data:
// every save then changes that bot.
const api: string = process.env.NNZ_API ?? 'http://localhost:5080'

// The bot checks a refresh's Origin against the address it sees, and it sees this hop's scheme (http).
// Behind the https tunnel the browser sends https://, so every refresh was refused and each load asked
// for sign-in. A same-host origin is passed on with the hop's scheme; any other origin is left as it is.
const sameHostOrigin: NonNullable<ProxyOptions['configure']> = (proxy) => {
  proxy.on('proxyReq', (proxyReq, req) => {
    const origin: string | undefined = req.headers.origin
    const host: string | undefined = req.headers.host
    if (!origin || !host || !URL.canParse(origin) || new URL(origin).host !== host) return
    proxyReq.setHeader('origin', `${new URL(api).protocol}//${host}`)
  })
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
    tailwindcss(),
    Icons({ compiler: 'vue3', customCollections: { nnz: designerIcons } }),
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
      '/api': { target: api, xfwd: true, configure: sameHostOrigin },
      '/hubs': { target: api, ws: true, xfwd: true },
      // The code editor page is an iframe that must share the dashboard's origin (both sides check it).
      '/editor': { target: api, xfwd: true },
      '/health': { target: api, xfwd: true },
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
