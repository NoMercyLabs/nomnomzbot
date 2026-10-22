// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { fileURLToPath, URL } from 'node:url';

import { defineConfig, type ProxyOptions } from 'vite';
import vue from '@vitejs/plugin-vue';
import vueDevTools from 'vite-plugin-vue-devtools';
import tailwindcss from '@tailwindcss/vite';
import { VitePWA } from 'vite-plugin-pwa';

import { designerIcons } from './vite-plugins/designerIcons';
import { hmrPageHost } from './vite-plugins/hmrPageHost';

// The bot the dev server talks to. NNZ_API points it at another bot, such as the live box, for real data:
// every save then changes that bot.
const api: string = process.env.NNZ_API ?? 'http://localhost:5080';

// The bot checks a refresh's Origin against the address it sees, and it sees this hop's scheme (http).
// Behind the https tunnel the browser sends https://, so every refresh was refused and each load asked
// for sign-in. A same-host origin is passed on with the hop's scheme; any other origin is left as it is.
const sameHostOrigin: NonNullable<ProxyOptions['configure']> = (proxy) => {
  proxy.on('proxyReq', (proxyReq, req) => {
    const origin: string | undefined = req.headers.origin;
    const host: string | undefined = req.headers.host;
    if (!origin || !host || !URL.canParse(origin) || new URL(origin).host !== host) return;

    // A login code is bound to the page's real origin (from its Referer), so it keeps the browser's one.
    if (req.url?.includes('/auth/web/exchange')) return;
    proxyReq.setHeader('origin', `${new URL(api).protocol}//${host}`);
  });
};

// A browser that carries the nnz_read_only=1 cookie (a test browser checking against the live box) cannot
// change it: every write stops here with a 404, and only sign-in passes. Other browsers are not touched.
// An audit script once clicked 37 "Delete message" buttons through this proxy.
const blockWrites: NonNullable<ProxyOptions['bypass']> = (req) => {
  const reads: boolean = req.method === 'GET' || req.method === 'HEAD' || req.method === 'OPTIONS';
  const readOnly: boolean = /(?:^|;\s*)nnz_read_only=1(?:;|$)/.test(req.headers.cookie ?? '');

  if (!readOnly || reads || req.url?.startsWith('/api/v1/auth/')) return;

  console.warn(`read only: refused ${req.method} ${req.url}`);

  return false;
};

// The bot's own paths: live data, sign-in, the editor page and the health check. A service worker never
// answers for them. Workbox copies these matchers into sw.js as text, so each one must carry everything it
// uses inside its own body (a constant defined out here would not exist there).
const isLivePath = ({ url }: { url: URL }): boolean =>
  /^\/(?:api\/|hubs\/|editor|health)/.test(url.pathname);

const isEmojiFont = ({ url }: { url: URL }): boolean =>
  url.pathname.startsWith('/assets/noto-color-emoji-');

const isPage = ({ request }: { request: Request }): boolean => request.mode === 'navigate';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    hmrPageHost(),
    vueDevTools(),
    tailwindcss(),
    designerIcons(),
    VitePWA({
      // The page asks first; a new deploy shows a toast and the user picks the moment to reload.
      registerType: 'prompt',
      // The dev server serves the manifest too, so an install from it gets the dark splash and bars.
      devOptions: { enabled: true, type: 'module' },
      includeAssets: ['favicon.ico', 'apple-touch-icon-180x180.png'],
      manifest: {
        name: 'NomNomzBot',
        short_name: 'NomNomzBot',
        description: 'Run your stream bot: commands, events, overlays and chat in one dashboard.',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        display_override: ['fullscreen', 'standalone'],
        theme_color: '#121212',
        background_color: '#121212',
        icons: [
          { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          {
            src: 'maskable-icon-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        // Only the hashed build files are precached (cache-first; a new hash is a new URL). index.html is
        // left out on purpose: it names the current hashes, so it is fetched network-first below.
        globPatterns: ['**/*.{js,css,woff2,png,svg,ico}'],
        // The color emoji font is 5.8 MB: too big to fetch up front on every install, so it is cached on
        // first use instead (cache-first; its name carries a hash, so it never goes stale).
        globIgnores: ['**/noto-color-emoji-*'],
        navigateFallback: null,
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          // Network only: live data, sign-in responses, the editor page and the health check.
          { urlPattern: isLivePath, handler: 'NetworkOnly' },
          {
            urlPattern: isEmojiFont,
            handler: 'CacheFirst',
            options: {
              cacheName: 'emoji-font',
              cacheableResponse: { statuses: [200] },
              expiration: { maxEntries: 8 },
            },
          },
          { urlPattern: isPage, handler: 'NetworkFirst', options: { cacheName: 'pages' } },
        ],
      },
    }),
  ],
  // 5090 is the registered OAuth origin; the proxy keeps the refresh cookie same-origin.
  // xfwd tells the bot the address the browser used (a phone on the network is not localhost), so its
  // sign-in callback and its refresh origin check match that address.
  server: {
    port: 5090,
    strictPort: true,
    host: true,
    // The https tunnel to this dev server, so a phone signs in by redirect.
    allowedHosts: ['vue.nomnomz.bot', 'dev.nomnomz.bot'],
    proxy: {
      '/api': { target: api, xfwd: true, configure: sameHostOrigin, bypass: blockWrites },
      '/hubs': { target: api, ws: true, xfwd: true },
      // The code editor page is an iframe that must share the dashboard's origin (both sides check it).
      '/editor': { target: api, xfwd: true },
      '/health': { target: api, xfwd: true },
      // OBS browser sources load /overlay on this host; without the proxy they get the dark dashboard page.
      '/overlay': { target: api, xfwd: true },
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
});
