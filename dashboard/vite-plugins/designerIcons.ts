// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
