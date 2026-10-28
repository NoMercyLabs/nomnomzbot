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

import { defineConfig, minimal2023Preset } from '@vite-pwa/assets-generator/config';

// The install icons are cut from the one brand mark (public/icon.svg, the designer's chat-bot glyph on the
// theme background). `npm run gen:pwa-icons` rewrites them; they are committed so a build needs no generator.
export default defineConfig({
  preset: minimal2023Preset,
  images: ['public/icon.svg'],
});
