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

import { fileURLToPath } from 'node:url';

import { defineConfig } from 'vite';

// Builds the overlay's video player into dist/vendor/nomercy-player.js: one classic browser script (the
// published player, its Twitch plugin and hls.js), served by the bot with the dashboard files. It runs after
// the dashboard build (npm run build-only), which empties dist first, so this one never does.
export default defineConfig({
  publicDir: false,
  build: {
    emptyOutDir: false,
    outDir: 'dist/vendor',
    lib: {
      entry: fileURLToPath(new URL('./src/overlay-player/entry.ts', import.meta.url)),
      formats: ['iife'],
      name: 'NomNomzPlayerBundle',
      fileName: () => 'nomercy-player.js',
    },
  },
});
