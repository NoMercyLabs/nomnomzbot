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

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));

// Bundled at runtime this module lives at <sdPlugin>/bin/plugin.js, one level above imgs/actions/ —
// the same files the manifest itself points to. Running the raw TS source (tests, dev) it lives at
// src/nowPlaying/ instead, two levels above tools/streamdeck/<sdPlugin>/imgs/actions/. Both are real,
// fixed layouts (not an open-ended search), so try the bundled path first and fall back to the source one.
const CANDIDATE_DIRS = [
  join(here, "..", "imgs", "actions"),
  join(here, "..", "..", "bot.nomnomzbot.streamdeck.music.sdPlugin", "imgs", "actions"),
];
const ACTIONS_ICON_DIR = CANDIDATE_DIRS.find((dir) => existsSync(dir)) ?? CANDIDATE_DIRS[0]!;

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
