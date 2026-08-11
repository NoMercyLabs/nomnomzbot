// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

// This module runs bundled as bin/plugin.js inside the .sdPlugin folder, so the icon SVGs it reads
// live one level up at imgs/actions/ — the same files the manifest itself points to.
const ACTIONS_ICON_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", "imgs", "actions");

const cache = new Map<string, string>();

/** Raw SVG markup for a recolored (white) action icon, by its manifest base name (e.g. "play"). */
export function loadIconMarkup(name: string): string {
  let markup = cache.get(name);
  if (markup === undefined) {
    markup = readFileSync(join(ACTIONS_ICON_DIR, `${name}.svg`), "utf8");
    cache.set(name, markup);
  }
  return markup;
}
