// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { defineConfig, minimal2023Preset } from '@vite-pwa/assets-generator/config';

// The install icons are cut from the one brand mark (public/icon.svg, the designer's chat-bot glyph on the
// theme background). `npm run gen:pwa-icons` rewrites them; they are committed so a build needs no generator.
export default defineConfig({
  preset: minimal2023Preset,
  images: ['public/icon.svg'],
});
