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

import type { PluginOption } from 'vite';
import Icons from 'unplugin-icons/vite';
import { FileSystemIconLoader } from 'unplugin-icons/loaders';

// The designer's icon pack (Line style), imported as `~icons/nnz/<name>`. The files carry a baked stroke
// color; it becomes currentColor so an icon takes the text color like any other glyph.
const lineIcons = FileSystemIconLoader('./src/assets/icons/line', (svg: string): string =>
  svg
    .replace(/\sstyle="[^"]*"/g, '')
    .replace(/stroke="#11181C"/gi, 'stroke="currentColor"')
    .replace(/fill="#11181C"/gi, 'fill="currentColor"'),
);

// The one icon setup, shared by the dashboard build and the public site build.
export function designerIcons(): PluginOption {
  return Icons({ compiler: 'vue3', customCollections: { nnz: lineIcons } });
}
