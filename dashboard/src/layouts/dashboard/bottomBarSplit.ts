// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { merge } from '@/lib/array';

import type { NavEntry } from './navigation';

export type BottomBarSplit = {
  bar: NavEntry[];
  sheet: NavEntry[];
};

// Five slots: all five hold pages when the pages fit, otherwise four pages and More.
const slots = 5;

// Pages fill the bar while there is room; the bottom pages win the slots once More is needed.
export function splitBottomBar(entries: NavEntry[]): BottomBarSplit {
  if (entries.length <= slots)
    return { bar: entries, sheet: [] };

  const ranked = merge(
    entries.filter((entry) => entry.bottom),
    entries.filter((entry) => !entry.bottom),
  );
  const bar = ranked.slice(0, slots - 1);

  return { bar, sheet: entries.filter((entry) => !bar.includes(entry)) };
}
